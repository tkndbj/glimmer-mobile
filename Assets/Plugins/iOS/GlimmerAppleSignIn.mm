// Sign in with Apple, bound for AppleSignIn.cs.
//
// This exists because Firebase refuses to do it. FirebaseAuth's generic IDP path — the one
// FederatedOAuthProvider drives, and the one this game uses for Google on both platforms and
// for Apple on Android — calls fatalError on iOS the moment the provider is apple.com:
//
//     FirebaseAuth/OAuthProvider.swift:83: Fatal error: Sign in with Apple is not supported
//     via generic IDP; You must use the Apple SDK for Sign in with Apple.
//
// It is a Swift fatalError, so it is not an exception and no managed try/catch can survive it;
// the process is gone. Apple requires the native AuthenticationServices sheet on their own
// platform, and there is no configuration that lifts it. So iOS gets forty lines of Objective-C
// and every other path is untouched.
//
// No managed callback crosses the boundary. The managed side polls, exactly as
// GlimmerAppTracking does, because holding a function pointer to managed code across a native
// callback needs a static MonoPInvokeCallback and is a documented way to crash under IL2CPP.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AuthenticationServices/AuthenticationServices.h>)
#import <AuthenticationServices/AuthenticationServices.h>
#define GLIMMER_HAS_ASA 1
#endif

// 0 idle, 1 pending, 2 succeeded, 3 failed, 4 cancelled by the player.
static int          gGlimmerAppleState = 0;
static NSString*    gGlimmerAppleToken = nil;
static NSString*    gGlimmerAppleCode  = nil;
static NSString*    gGlimmerAppleError = nil;

#ifdef GLIMMER_HAS_ASA

API_AVAILABLE(ios(13.0))
@interface GlimmerAppleSignInDelegate : NSObject <ASAuthorizationControllerDelegate,
                                                  ASAuthorizationControllerPresentationContextProviding>
@end

// Retained for the life of the request. ASAuthorizationController holds its delegate weakly,
// so a local would be released the instant the calling function returned and the sheet would
// answer nobody — a hang rather than a crash, which is worse to diagnose.
static id gGlimmerAppleDelegate = nil;

@implementation GlimmerAppleSignInDelegate

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller {
    // Prefer the active foreground scene's key window. keyWindow on UIApplication is
    // deprecated and, in a multi-scene app, can answer for the wrong one.
    if (@available(iOS 13.0, *)) {
        for (UIScene *scene in [UIApplication sharedApplication].connectedScenes) {
            if (scene.activationState != UISceneActivationStateForegroundActive) continue;
            if (![scene isKindOfClass:[UIWindowScene class]]) continue;
            for (UIWindow *window in ((UIWindowScene *)scene).windows) {
                if (window.isKeyWindow) return window;
            }
        }
    }
    return [UIApplication sharedApplication].windows.firstObject;
}

- (void)authorizationController:(ASAuthorizationController *)controller
   didCompleteWithAuthorization:(ASAuthorization *)authorization {

    if (![authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]]) {
        gGlimmerAppleError = @"unexpected credential type";
        gGlimmerAppleState = 3;
        return;
    }

    ASAuthorizationAppleIDCredential *credential =
        (ASAuthorizationAppleIDCredential *)authorization.credential;

    NSData *identityToken = credential.identityToken;
    if (identityToken == nil) {
        // Documented as possible, and it is the one failure that looks like success from the
        // player's side — they tapped through the sheet and there is simply nothing to send.
        gGlimmerAppleError = @"Apple returned no identity token";
        gGlimmerAppleState = 3;
        return;
    }

    NSString *jwt = [[NSString alloc] initWithData:identityToken encoding:NSUTF8StringEncoding];
    if (jwt.length == 0) {
        gGlimmerAppleError = @"identity token was not readable";
        gGlimmerAppleState = 3;
        return;
    }

    // Firebase needs the authorization code as well as the identity token, and it is not
    // optional however much the parameter is named "accessToken". A credential carrying a
    // perfectly valid, correctly-nonced identity token and no code is refused with
    // "Invalid OAuth response from apple.com" — the same sentence Firebase uses for a
    // malformed token, which is what makes this so hard to tell apart from a broken nonce.
    NSData *authorizationCode = credential.authorizationCode;
    if (authorizationCode != nil) {
        gGlimmerAppleCode = [[NSString alloc] initWithData:authorizationCode
                                                  encoding:NSUTF8StringEncoding];
    }

    gGlimmerAppleToken = jwt;
    gGlimmerAppleState = 2;
}

- (void)authorizationController:(ASAuthorizationController *)controller
           didCompleteWithError:(NSError *)error {

    // Cancellation is separated from failure all the way up: closing the sheet is the most
    // ordinary outcome there is, and reporting it as an error is how a player is told their
    // progress could not be saved because they changed their mind.
    if (error.code == ASAuthorizationErrorCanceled) {
        gGlimmerAppleError = @"cancelled";
        gGlimmerAppleState = 4;
        return;
    }

    gGlimmerAppleError = error.localizedDescription ?: @"unknown error";
    gGlimmerAppleState = 3;
}

@end

#endif

extern "C" {

int GlimmerAppleSignInState() { return gGlimmerAppleState; }

const char* GlimmerAppleSignInToken() {
    return gGlimmerAppleToken ? [gGlimmerAppleToken UTF8String] : "";
}

const char* GlimmerAppleSignInCode() {
    return gGlimmerAppleCode ? [gGlimmerAppleCode UTF8String] : "";
}

const char* GlimmerAppleSignInError() {
    return gGlimmerAppleError ? [gGlimmerAppleError UTF8String] : "";
}

// hashedNonce is the SHA-256 of the raw nonce, lowercase hex. Apple signs it into the JWT and
// Firebase compares it against the raw one it is handed separately, which is what stops a
// token captured from one session being replayed into another.
void GlimmerAppleSignInStart(const char* hashedNonce) {
    gGlimmerAppleToken = nil;
    gGlimmerAppleCode  = nil;
    gGlimmerAppleError = nil;
    gGlimmerAppleState = 1;

#ifdef GLIMMER_HAS_ASA
    if (@available(iOS 13.0, *)) {
        ASAuthorizationAppleIDProvider *provider = [[ASAuthorizationAppleIDProvider alloc] init];
        ASAuthorizationAppleIDRequest *request = [provider createRequest];
        request.requestedScopes = @[ASAuthorizationScopeFullName, ASAuthorizationScopeEmail];
        if (hashedNonce != NULL) {
            request.nonce = [NSString stringWithUTF8String:hashedNonce];
        }

        GlimmerAppleSignInDelegate *delegate = [[GlimmerAppleSignInDelegate alloc] init];
        gGlimmerAppleDelegate = delegate;

        ASAuthorizationController *controller =
            [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
        controller.delegate = delegate;
        controller.presentationContextProvider = delegate;

        // The sheet must be presented on the main thread. This is already called from Unity's
        // main thread, but the dispatch costs nothing and removes the assumption.
        dispatch_async(dispatch_get_main_queue(), ^{
            [controller performRequests];
        });
        return;
    }
#endif

    gGlimmerAppleError = @"Sign in with Apple needs iOS 13 or newer";
    gGlimmerAppleState = 3;
}

}
