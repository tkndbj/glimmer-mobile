// The share sheet, bound for ShareSheet.cs.
//
// One function and no state: UIActivityViewController is handed one string and presented
// from Unity's own view controller. Nothing comes back across the boundary — which app the
// sentence went to is not this game's business, and the managed side treats the tap as done
// the moment the sheet is up.
//
// The popover anchor is set unconditionally. On an iPhone it is ignored; on an iPad a share
// sheet presented without one is an exception at runtime, which is the kind of fault that
// ships because every test device was a phone.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

extern "C" UIViewController* UnityGetGLViewController();

extern "C" {

void GlimmerShare(const char* text) {
    NSString* sentence = [NSString stringWithUTF8String:(text ? text : "")];
    if (sentence.length == 0) return;

    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* host = UnityGetGLViewController();
        if (host == nil) return;

        UIActivityViewController* sheet =
            [[UIActivityViewController alloc] initWithActivityItems:@[sentence]
                                              applicationActivities:nil];

        UIPopoverPresentationController* popover = sheet.popoverPresentationController;
        if (popover != nil) {
            popover.sourceView = host.view;
            popover.sourceRect = CGRectMake(CGRectGetMidX(host.view.bounds),
                                            CGRectGetMidY(host.view.bounds), 1, 1);
            popover.permittedArrowDirections = 0;
        }

        [host presentViewController:sheet animated:YES completion:nil];
    });
}

}
