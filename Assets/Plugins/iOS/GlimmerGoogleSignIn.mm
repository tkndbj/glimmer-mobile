// Google sign-in through ASWebAuthenticationSession, bound for GoogleSignIn.cs.
//
// Firebase's generic IDP path — the one FederatedOAuthProvider drives — does not work for
// google.com on iOS through the Unity/C++ SDK. It does not crash the way apple.com does; it
// simply never comes back. The consent screen appears, the account is chosen, and the app is
// left holding a blank web view for ever with no error on either side. Same weakness as the
// Apple fatalError, failing quietly instead of loudly, which is worse to diagnose.
//
// So iOS drives the OAuth flow itself. ASWebAuthenticationSession is the API Apple provides
// for exactly this and the one Google's own SDK uses underneath: it presents the consent
// page outside the app's process, shares the system cookie jar (so an account already signed
// in on the device is offered rather than retyped), and hands back the redirect URL.
//
// No managed callback crosses the boundary — the managed side polls, for the reason
// GlimmerAppTracking and GlimmerAppleSignIn give.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AuthenticationServices/AuthenticationServices.h>)
#import <AuthenticationServices/AuthenticationServices.h>
#define GLIMMER_HAS_ASWEB 1
#endif

// 0 idle, 1 pending, 2 succeeded, 3 failed, 4 cancelled by the player.
static int       gGlimmerGoogleState = 0;
static NSString* gGlimmerGoogleUrl   = nil;
static NSString* gGlimmerGoogleError = nil;

#ifdef GLIMMER_HAS_ASWEB

API_AVAILABLE(ios(13.0))
@interface GlimmerGoogleAnchor : NSObject <ASWebAuthenticationPresentationContextProviding>
@end

// Retained for the life of the request, and so is the session itself: both are held only
// weakly by the system, and a local would be released the moment the calling function
// returned — the sheet would appear and answer nobody.
static id gGlimmerGoogleAnchor  = nil;
static id gGlimmerGoogleSession = nil;

@implementation GlimmerGoogleAnchor

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session {
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

@end

#endif

extern "C" {

// The iOS OAuth client id, read out of the GoogleService-Info.plist this app already
// ships rather than written down a second time. Firebase's managed AppOptions does not
// expose it, and a copy in C# is a copy that can come to disagree with the plist the same
// tool generated — which would show up as a redirect scheme that no longer matches.
const char* GlimmerGoogleClientId() {
    static NSString* cached = nil;

    if (cached == nil) {
        NSString *path = [[NSBundle mainBundle] pathForResource:@"GoogleService-Info"
                                                         ofType:@"plist"];
        if (path != nil) {
            NSDictionary *options = [NSDictionary dictionaryWithContentsOfFile:path];
            id value = options[@"CLIENT_ID"];
            if ([value isKindOfClass:[NSString class]]) cached = (NSString *)value;
        }
    }

    return cached ? [cached UTF8String] : "";
}

int GlimmerGoogleSignInState() { return gGlimmerGoogleState; }

const char* GlimmerGoogleSignInUrl() {
    return gGlimmerGoogleUrl ? [gGlimmerGoogleUrl UTF8String] : "";
}

const char* GlimmerGoogleSignInError() {
    return gGlimmerGoogleError ? [gGlimmerGoogleError UTF8String] : "";
}

// authUrl is the full Google authorisation URL including the PKCE challenge; scheme is the
// reversed client id the redirect comes back on, which iOS matches to hand control back.
void GlimmerGoogleSignInStart(const char* authUrl, const char* scheme) {
    gGlimmerGoogleUrl   = nil;
    gGlimmerGoogleError = nil;
    gGlimmerGoogleState = 1;

#ifdef GLIMMER_HAS_ASWEB
    if (@available(iOS 13.0, *)) {
        if (authUrl == NULL || scheme == NULL) {
            gGlimmerGoogleError = @"no authorisation url";
            gGlimmerGoogleState = 3;
            return;
        }

        NSURL *url = [NSURL URLWithString:[NSString stringWithUTF8String:authUrl]];
        NSString *callbackScheme = [NSString stringWithUTF8String:scheme];

        if (url == nil) {
            gGlimmerGoogleError = @"authorisation url was not parseable";
            gGlimmerGoogleState = 3;
            return;
        }

        GlimmerGoogleAnchor *anchor = [[GlimmerGoogleAnchor alloc] init];
        gGlimmerGoogleAnchor = anchor;

        ASWebAuthenticationSession *session =
            [[ASWebAuthenticationSession alloc] initWithURL:url
                                          callbackURLScheme:callbackScheme
                                          completionHandler:^(NSURL *callback, NSError *error) {
            if (error != nil) {
                // Closing the sheet is the commonest outcome and is not a failure. It is
                // separated all the way up so the screen never reports "something went
                // wrong" to somebody who simply changed their mind.
                if (error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin) {
                    gGlimmerGoogleError = @"cancelled";
                    gGlimmerGoogleState = 4;
                } else {
                    gGlimmerGoogleError = error.localizedDescription ?: @"unknown error";
                    gGlimmerGoogleState = 3;
                }
                gGlimmerGoogleSession = nil;
                return;
            }

            if (callback == nil) {
                gGlimmerGoogleError = @"Google returned no redirect";
                gGlimmerGoogleState = 3;
                gGlimmerGoogleSession = nil;
                return;
            }

            gGlimmerGoogleUrl   = callback.absoluteString;
            gGlimmerGoogleState = 2;
            gGlimmerGoogleSession = nil;
        }];

        session.presentationContextProvider = anchor;

        // Share the system cookie jar, so an account already signed in on this device is
        // offered as a tap rather than a password. Turning this off is what makes a native
        // flow feel worse than the web one it replaces.
        session.prefersEphemeralWebBrowserSession = NO;

        gGlimmerGoogleSession = session;

        dispatch_async(dispatch_get_main_queue(), ^{
            if (![session start]) {
                gGlimmerGoogleError = @"the authentication session refused to start";
                gGlimmerGoogleState = 3;
                gGlimmerGoogleSession = nil;
            }
        });
        return;
    }
#endif

    gGlimmerGoogleError = @"Google sign-in needs iOS 13 or newer";
    gGlimmerGoogleState = 3;
}

}
