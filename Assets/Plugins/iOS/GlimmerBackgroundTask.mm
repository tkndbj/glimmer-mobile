// Background time for the cloud departure, bound for BackgroundGrace.cs.
//
// iOS suspends an app a few seconds after it leaves the foreground. The departure - a read, a
// merge and a write of the player's save, carried on the thread pool - normally finishes long
// before that, and this is what makes "normally" into "always, within about thirty seconds".
//
// Two functions and one table. Begin answers the task's identifier, or 0 when the system
// refuses (UIBackgroundTaskInvalid); End ends it. Either the managed side or the expiry handler
// may end a task first, so both go through one idempotent path guarded by a lock: ending a task
// twice is a documented misuse the system logs about, and ending an invalid one is a no-op we
// never rely on.
//
// Callable from any thread. beginBackgroundTask is documented as thread-safe; End may arrive on
// a thread-pool thread while the main thread is paused, which is the whole point.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

static NSMutableSet<NSNumber*>* GlimmerLiveTasks(void) {
    static NSMutableSet<NSNumber*>* live;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ live = [NSMutableSet set]; });
    return live;
}

static void GlimmerFinishTask(UIBackgroundTaskIdentifier task) {
    if (task == UIBackgroundTaskInvalid) return;

    NSNumber* key = @(task);
    NSMutableSet<NSNumber*>* live = GlimmerLiveTasks();

    BOOL owned = NO;
    @synchronized (live) {
        if ([live containsObject:key]) {
            [live removeObject:key];
            owned = YES;
        }
    }

    if (owned) [[UIApplication sharedApplication] endBackgroundTask:task];
}

extern "C" {

long long GlimmerBeginBackgroundTask(void) {
    __block UIBackgroundTaskIdentifier task = UIBackgroundTaskInvalid;

    task = [[UIApplication sharedApplication]
        beginBackgroundTaskWithName:@"GlimmerCloudDeparture"
                  expirationHandler:^{
                      // Out of time. The departure is abandoned where it stands; the next sync
                      // carries the same work, so nothing is lost but the head start.
                      GlimmerFinishTask(task);
                  }];

    if (task == UIBackgroundTaskInvalid) return 0;

    @synchronized (GlimmerLiveTasks()) {
        [GlimmerLiveTasks() addObject:@(task)];
    }

    return (long long)task;
}

void GlimmerEndBackgroundTask(long long task) {
    GlimmerFinishTask((UIBackgroundTaskIdentifier)task);
}

}
