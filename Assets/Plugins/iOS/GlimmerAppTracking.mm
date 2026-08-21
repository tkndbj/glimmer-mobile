// App Tracking Transparency, bound for AppTrackingPrompt.cs.
//
// Two functions and no state. The status is read from the framework every time rather than
// cached here, because iOS owns the answer and can change it while the app is running — a
// player who revokes tracking in Settings comes back to a process whose cached copy would be
// wrong for the rest of its life.
//
// Weak-linked so the app still launches on an iOS old enough to have no framework: the class
// is looked up by name and a nil result reports notDetermined, which the managed side never
// reaches because it checks the OS version first.

#import <Foundation/Foundation.h>

#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#define GLIMMER_HAS_ATT 1
#endif

extern "C" {

// Mirrors ATTrackingManagerAuthorizationStatus, and TrackingStatus in C# mirrors it again:
// 0 notDetermined, 1 restricted, 2 denied, 3 authorized.
int GlimmerTrackingStatus() {
#ifdef GLIMMER_HAS_ATT
    if (@available(iOS 14, *)) {
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    }
#endif
    return 0;
}

// Fire and forget. The completion handler is deliberately empty: the managed side polls
// GlimmerTrackingStatus rather than being called back, which avoids holding a managed
// function pointer across the boundary. See the note in AppTrackingPrompt.RequestAsync.
void GlimmerRequestTracking() {
#ifdef GLIMMER_HAS_ATT
    if (@available(iOS 14, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:
            ^(ATTrackingManagerAuthorizationStatus status) { (void)status; }];
    }
#endif
}

}
