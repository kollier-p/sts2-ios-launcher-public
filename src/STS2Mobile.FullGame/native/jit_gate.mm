// Adapted from TheUnknownThing/StS2-Launcher-iOS ios/jit/JitGate.mm
// Copyright (c) 2026 Eky; MIT licensed (experimental/jit/LICENSE.upstream.txt).
// Compiled but inert unless STS2_JIT_RUNTIME is explicitly enabled.
// The launcher downloads its own Steam game content; this gate checks only
// debugger/JIT readiness before Godot and Mono runtime initialization.
#ifdef STS2_JIT_RUNTIME
#import <UIKit/UIKit.h>
#import <objc/runtime.h>
#include <unistd.h>

extern "C" int csops(pid_t, unsigned int, void *, size_t);
static BOOL (*originalLaunch)(id, SEL, UIApplication *, NSDictionary *);
static NSMutableDictionary<NSString *, NSValue *> *lifecycleMethods;
static BOOL engineStarted;

@interface STS2JitGate : NSObject
@property(nonatomic, strong) id service;
@property(nonatomic, strong) UIApplication *application;
@property(nonatomic, copy) NSDictionary *launchOptions;
@property(nonatomic, strong) UIWindow *window;
@property(nonatomic, strong) UILabel *notice;
@property(nonatomic, strong) NSTimer *timer;
@property(nonatomic, copy) NSString *documents;
- (void)start;
- (void)checkReadiness;
@end

static STS2JitGate *startupGate;

static BOOL LaunchWhenReady(id service, SEL selector, UIApplication *application, NSDictionary *options) {
    startupGate = [STS2JitGate new];
    startupGate.service = service;
    startupGate.application = application;
    startupGate.launchOptions = options;
    [startupGate start];
    return YES;
}

static void ForwardLifecycle(id service, SEL selector, UIApplication *application) {
    // Godot's lifecycle callbacks dereference its OS singleton before checking it.
    if (!engineStarted) return;
    auto original = (void (*)(id, SEL, UIApplication *))lifecycleMethods[NSStringFromSelector(selector)].pointerValue;
    original(service, selector, application);
}

@implementation STS2JitGate
+ (void)load {
    // Main::setup loads the PCK during didFinishLaunching, before setupView runs.
    Class service = NSClassFromString(@"GDTAppDelegateService");
    Method launch = class_getInstanceMethod(service, @selector(application:didFinishLaunchingWithOptions:));
    if (!launch) abort();
    originalLaunch = (BOOL (*)(id, SEL, UIApplication *, NSDictionary *))method_setImplementation(launch, (IMP)LaunchWhenReady);
    lifecycleMethods = [NSMutableDictionary new];
    for (NSString *name in @[@"applicationDidBecomeActive:", @"applicationWillResignActive:",
             @"applicationDidEnterBackground:", @"applicationWillEnterForeground:",
             @"applicationDidReceiveMemoryWarning:", @"applicationWillTerminate:"]) {
        Method method = class_getInstanceMethod(service, NSSelectorFromString(name));
        if (!method) abort();
        lifecycleMethods[name] = [NSValue valueWithPointer:(const void *)method_setImplementation(method, (IMP)ForwardLifecycle)];
    }
}

- (void)start {
    self.documents = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES).firstObject;
    UIViewController *controller = [UIViewController new];
    controller.view.backgroundColor = UIColor.systemBackgroundColor;
    self.notice = [UILabel new];
    self.notice.translatesAutoresizingMaskIntoConstraints = NO;
    self.notice.textColor = UIColor.labelColor;
    self.notice.font = [UIFont preferredFontForTextStyle:UIFontTextStyleTitle3];
    self.notice.textAlignment = NSTextAlignmentCenter;
    self.notice.numberOfLines = 0;
    [controller.view addSubview:self.notice];
    UILayoutGuide *area = controller.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.notice.centerYAnchor constraintEqualToAnchor:area.centerYAnchor],
        [self.notice.leadingAnchor constraintEqualToAnchor:area.leadingAnchor constant:24],
        [self.notice.trailingAnchor constraintEqualToAnchor:area.trailingAnchor constant:-24],
    ]];
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    self.window.rootViewController = controller;
    [self.service setValue:self.window forKey:@"window"];
    [self.window makeKeyAndVisible];

    __weak STS2JitGate *gate = self;
    self.timer = [NSTimer scheduledTimerWithTimeInterval:0.5 repeats:YES block:^(NSTimer *timer) {
        [gate checkReadiness];
    }];
    self.notice.text = @"StS2 JIT\n\nPreparing...";
}

- (void)checkReadiness {
    if (self.application.applicationState != UIApplicationStateActive) return;
    uint32_t flags = 0;
    if (csops(getpid(), 0, &flags, sizeof(flags)) != 0 || !(flags & 0x10000000)) {
        self.notice.text = @"StS2 JIT\n\nGame content is ready.\n\n"
            @"Enable JIT for this app with your JIT tool or a Mac debugger.\n"
            @"The game will start automatically when JIT is ready.\n\n"
            @"JIT must be enabled again after the app process restarts.";
        return;
    }
    [self.timer invalidate];
    self.timer = nil;
    self.notice.text = @"StS2 JIT\n\nStarting game...";
    originalLaunch(self.service, @selector(application:didFinishLaunchingWithOptions:), self.application, self.launchOptions);
    engineStarted = YES;
    // The initial active notification was deliberately withheld while waiting.
    ForwardLifecycle(self.service, @selector(applicationDidBecomeActive:), self.application);
    self.window.hidden = YES;
    startupGate = nil;
}
@end

#endif // STS2_JIT_RUNTIME
