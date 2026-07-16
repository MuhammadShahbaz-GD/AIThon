#import <UIKit/UIKit.h>

extern "C" {
    void ATH_Init() {}
    bool ATH_IsSupported() { return true; }
    void ATH_Impact(int style, float intensity) {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackStyle value = UIImpactFeedbackStyleMedium;
            if (style == 0) value = UIImpactFeedbackStyleLight; else if (style == 2) value = UIImpactFeedbackStyleHeavy;
            else if (@available(iOS 13.0, *)) { if (style == 3) value = UIImpactFeedbackStyleSoft; else if (style == 4) value = UIImpactFeedbackStyleRigid; }
            UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:value]; [generator prepare];
            if (@available(iOS 13.0, *)) [generator impactOccurredWithIntensity:MAX(0.0, MIN(1.0, intensity))]; else [generator impactOccurred];
        }
    }
    void ATH_Notification(int type) { if (@available(iOS 10.0, *)) { UINotificationFeedbackGenerator *generator = [UINotificationFeedbackGenerator new]; [generator prepare]; UINotificationFeedbackType value = type == 0 ? UINotificationFeedbackTypeSuccess : type == 1 ? UINotificationFeedbackTypeWarning : UINotificationFeedbackTypeError; [generator notificationOccurred:value]; } }
    void ATH_Selection() { if (@available(iOS 10.0, *)) { UISelectionFeedbackGenerator *generator = [UISelectionFeedbackGenerator new]; [generator prepare]; [generator selectionChanged]; } }
    void ATH_Cancel() {}
}
