#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();
extern void UnitySendMessage(const char* obj, const char* method, const char* msg);

static void DCGO_SendPickedPath(NSString* path)
{
    const char* cpath = path != nil ? path.UTF8String : "";
    UnitySendMessage("ReplayFileBridge", "OnReplayFilePicked", cpath != NULL ? cpath : "");
}

@interface DCGOReplayDocDelegate : NSObject<UIDocumentPickerDelegate>
@end

@implementation DCGOReplayDocDelegate

- (void)documentPicker:(UIDocumentPickerViewController*)controller didPickDocumentsAtURLs:(NSArray<NSURL*>*)urls
{
    NSURL* url = urls.firstObject;
    if (url == nil)
    {
        DCGO_SendPickedPath(@"");
        return;
    }

    BOOL scoped = [url startAccessingSecurityScopedResource];
    NSString* name = url.lastPathComponent;
    if (name.length == 0)
    {
        name = @"import.dcgoreplay";
    }

    NSString* dest = [NSTemporaryDirectory() stringByAppendingPathComponent:name];
    NSFileManager* files = [NSFileManager defaultManager];
    BOOL copied = NO;
    if ([url.path isEqualToString:dest])
    {
        copied = YES;
    }
    else
    {
        [files removeItemAtPath:dest error:nil];
        NSError* error = nil;
        copied = [files copyItemAtURL:url toURL:[NSURL fileURLWithPath:dest] error:&error];
    }

    if (scoped)
    {
        [url stopAccessingSecurityScopedResource];
    }

    DCGO_SendPickedPath(copied ? dest : @"");
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController*)controller
{
    DCGO_SendPickedPath(@"");
}

@end

static DCGOReplayDocDelegate* sDCGOReplayDocDelegate;

static void DCGO_Present(UIViewController* controller)
{
    UIViewController* root = UnityGetGLViewController();
    if (root == nil)
    {
        return;
    }

    if (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad)
    {
        controller.popoverPresentationController.sourceView = root.view;
        controller.popoverPresentationController.sourceRect = CGRectMake(CGRectGetMidX(root.view.bounds), CGRectGetMidY(root.view.bounds), 1.0, 1.0);
        controller.popoverPresentationController.permittedArrowDirections = 0;
    }

    [root presentViewController:controller animated:YES completion:nil];
}

extern "C" void DCGO_ReplayShareFile(const char* path)
{
    if (path == NULL)
    {
        return;
    }

    NSString* filePath = [NSString stringWithUTF8String:path];
    NSURL* url = [NSURL fileURLWithPath:filePath];
    if (url == nil || ![[NSFileManager defaultManager] fileExistsAtPath:filePath])
    {
        return;
    }

    UIActivityViewController* activity = [[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];
    DCGO_Present(activity);
}

extern "C" void DCGO_ReplayPickFile()
{
    if (sDCGOReplayDocDelegate == nil)
    {
        sDCGOReplayDocDelegate = [DCGOReplayDocDelegate new];
    }

    NSArray* types = @[@"public.item", @"public.data", @"public.content", @"public.json", @"public.text"];
    UIDocumentPickerViewController* picker = [[UIDocumentPickerViewController alloc] initWithDocumentTypes:types inMode:UIDocumentPickerModeImport];
    picker.delegate = sDCGOReplayDocDelegate;
    picker.modalPresentationStyle = UIModalPresentationFormSheet;
    DCGO_Present(picker);
}
