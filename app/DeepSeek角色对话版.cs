// DeepSeek 小鲸鱼独立版：WPF 高 DPI 悬浮界面。
// 高清透明立绘 + 局部柔性网格，按屏幕刷新节奏最高目标 120 帧。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeepSeekBalanceViewer;
using DeepSeekWhaleStandalone;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DeepSeekWhaleWpf
{
    public sealed class WhaleWindow : Window
    {
        private readonly BalanceClient client = new BalanceClient();
        private readonly WhaleSettings settings;
        private readonly UsageLedger ledger;
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer();
        private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        private readonly Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
        private readonly ContextMenu widgetMenu = new ContextMenu();
        private readonly PetSprite mascot = new PetSprite();
        private readonly Border bubble = new Border();
        private readonly Grid scene = new Grid();
        private readonly Border bubbleShadow = new Border();
        private readonly Border tail = new Border();
        private readonly Border speech = new Border();
        private readonly TextBox speechText = new TextBox();
        private readonly TextBlock speechCaption = new TextBlock();
        private readonly TextBlock speechContext = new TextBlock();
        private readonly Canvas chargeLayer = new Canvas { IsHitTestVisible = false };
        private readonly List<ChargeParticle> charges = new List<ChargeParticle>();
        private readonly PetSprite departingMascot = new PetSprite { IsHitTestVisible = false, Opacity = 0 };
        private readonly SoftValue softHead = new SoftValue(0), softNod = new SoftValue(0), softHair = new SoftValue(0), softLimb = new SoftValue(0), softAngle = new SoftValue(0);
        private readonly SoftValue softLookX = new SoftValue(0), softLookY = new SoftValue(0);
        private PetMotion displayedMotion;
        private double lookX,lookY,lastMotionTime=-1,nextFrameAt;
        private bool sleeping, touchedHead, offlinePreview;
        private readonly List<Action> companionChecks = new List<Action>();
        private readonly Dictionary<string, BitmapSource[]> poseImages = new Dictionary<string, BitmapSource[]>();
        private readonly RotateTransform bodyRotation = new RotateTransform();
        private readonly TranslateTransform bodyMovement = new TranslateTransform();
        private readonly List<MenuItem> poseMenuItems = new List<MenuItem>();
        private readonly List<Forms.ToolStripMenuItem> trayPoseMenuItems = new List<Forms.ToolStripMenuItem>();
        private string currentPose = "sit", activeAction = "", queuedAction = "";
        private double actionStart, lastPoseChange, transitionStart = -10;
        private double headX = .5, headY = .02;
        private string pendingPose = "";
        private double nextAmbient = 18, lastPetClick = -10;
        private int clickBurst;
        private readonly TranslateTransform speechFollow = new TranslateTransform();
        private double oldSpeechOffset;
        private readonly Grid petInput = new Grid();
        private readonly DispatcherTimer speechTimer = new DispatcherTimer();
        private string fullSpeech = "";
        private int[] speechOffsets = new int[0];
        private int speechProgress;
        private MenuItem dataToggle;
        private Forms.ToolStripMenuItem trayDataToggle;
        private readonly TextBlock brand = new TextBlock();
        private readonly Button menuButton = new Button();
        private readonly List<MenuItem> textSizeItems = new List<MenuItem>();
        private readonly List<Forms.ToolStripMenuItem> trayTextSizeItems = new List<Forms.ToolStripMenuItem>();
        private readonly TextBlock titleText = new TextBlock();
        private readonly TextBlock amountText = new TextBlock();
        private readonly TextBlock detailText = new TextBlock();
        private readonly TextBlock footerText = new TextBlock();
        private readonly StackPanel chatMessages = new StackPanel();
        private readonly ScrollViewer chatScroll = new ScrollViewer();
        private readonly TextBox chatInput = new TextBox();
        private readonly TextBlock inputHint = new TextBlock();
        private readonly Border chatInputFrame = new Border();
        private readonly Border statusFrame = new Border();
        private readonly Button refreshButton = new Button();
        private readonly Button sendButton = new Button();
        private readonly List<ChatTurn> chatHistory = new List<ChatTurn>();
        private readonly ChatClient chatClient = new ChatClient();
        private readonly Stopwatch animationClock = new Stopwatch();
        private static readonly string[] ClickClips = { "tilt", "twirl", "pet", "hop" };
        private BitmapSource eyesOpen;
        private BitmapSource eyesClosed;
        private readonly ScaleTransform idleBreath = new ScaleTransform(.92, .92);
        private int reactionIndex;
        private bool chatBusy;
        private int chatEpoch;
        private double bodyTextSize = 14;
        private Drawing.Icon ownedIcon;
        private string sessionKey;
        private BalanceResult latest;
        private DateTime lastUpdated;
        private string status = "点击角色查询余额";
        private bool busy;
        private bool alertActive;
        private DateTime lastBudgetAlertDay;
        private int bubblePage;
        private bool dragging;
        private bool pressed;
        private object pressedSource;
        private Drawing.Point cursorStart;
        private Drawing.Point lastDragCursor;

        public WhaleWindow() : this(false) { }

        // Offline preview/verification never loads keys, starts timers or creates a tray icon.
        internal WhaleWindow(bool previewMode)
        {
            offlinePreview=previewMode;
            settings = previewMode ? new WhaleSettings() : SettingsStore.Load();
            if (settings.RefreshSeconds < 30 || settings.RefreshSeconds > 300) settings.RefreshSeconds = 60;
            if (settings.ScalePercent < 40 || settings.ScalePercent > 220) settings.ScalePercent = 100;
            if (settings.TextSize < 13 || settings.TextSize > 18) settings.TextSize = 14;
            if(settings.TargetFps!=30&&settings.TargetFps!=60&&settings.TargetFps!=120) settings.TargetFps=120;
            if (settings.PetPose != "sit" && settings.PetPose != "stand" && settings.PetPose != "prone") settings.PetPose = "sit";
            ledger = UsageLedger.FromSettings(settings);
            sessionKey = SettingsStore.UnprotectKey(settings.ProtectedKey);

            Title = "DeepSeek 小鲸鱼独立版";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            // Layered transparent windows cannot use ClearType correctly; grayscale keeps glyph edges clean.
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

            BuildMainVisual();
            if (previewMode) return;
            BuildMenus();
            BuildTray();
            CompositionTarget.Rendering += OnAnimationFrame;

            refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
            refreshTimer.Tick += async (s, e) => await RefreshBalanceAsync();
            refreshTimer.Start();

            Loaded += (s, e) => PlaceInitialWindow();
            ContentRendered += async (s, e) =>
            {
                if (sessionKey.Length == 0) ShowSettings();
                if (sessionKey.Length > 0) await RefreshBalanceAsync();
            };
            Closed += (s, e) =>
            {
                refreshTimer.Stop();
                speechTimer.Stop();
                CompositionTarget.Rendering -= OnAnimationFrame;
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                if (ownedIcon != null) ownedIcon.Dispose();
            };
        }

        private void BuildMainVisual()
        {
            Grid root = scene;
            root.UseLayoutRounding = true;
            root.SnapsToDevicePixels = true;
            Content = root;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            mascot.Width = 300;
            mascot.Height = 300;
            mascot.Stretch = Stretch.Uniform;
            mascot.HorizontalAlignment = HorizontalAlignment.Right;
            mascot.VerticalAlignment = VerticalAlignment.Bottom;
            mascot.Margin = new Thickness(0, 0, 0, 2);
            mascot.Cursor = Cursors.Hand;
            mascot.ToolTip = "点头部摸摸她，点身体互动；滚轮缩放，拖动移动；右键调整陪伴模式";
            mascot.RenderTransformOrigin = new Point(0.5, 0.5);
            TransformGroup transforms = new TransformGroup();
            transforms.Children.Add(idleBreath);
            transforms.Children.Add(bodyRotation);
            transforms.Children.Add(bodyMovement);
            mascot.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(mascot, BitmapScalingMode.HighQuality);
            var sitting = LoadMascot();
            poseImages["sit"] = new[] { sitting, LoadPose("eyes-closed.png") ?? sitting };
            foreach (string pose in new[] { "stand", "prone" }) {
                var open = LoadPose(pose + ".png");
                if(open != null) poseImages[pose] = new[] { open, LoadPose(pose + "-closed.png") ?? open };
            }
            currentPose = poseImages.ContainsKey(settings.PetPose) ? settings.PetPose : "sit";
            eyesOpen = poseImages[currentPose][0]; eyesClosed = poseImages[currentPose][1];
            mascot.Source = eyesOpen;
            animationClock.Start();
            if (mascot.Source == null) status = "角色图片未打包，仍可从托盘查询";
            root.Children.Add(departingMascot);
            root.Children.Add(mascot);

            // Shadow is a separate empty sibling: text is never flattened into an effect bitmap.
            bubbleShadow.Background = Brushes.White;
            bubbleShadow.CornerRadius = new CornerRadius(20);
            bubbleShadow.HorizontalAlignment = HorizontalAlignment.Left;
            bubbleShadow.VerticalAlignment = VerticalAlignment.Top;
            bubbleShadow.IsHitTestVisible = false;
            bubbleShadow.Effect = new DropShadowEffect {
                Color = Color.FromRgb(32, 58, 85), BlurRadius = 12, ShadowDepth = 3, Opacity = .12
            };
            root.Children.Add(bubbleShadow);
            tail.Width = 15;
            tail.Height = 15;
            tail.Background = Brushes.White;
            tail.BorderBrush = new SolidColorBrush(Color.FromRgb(206, 222, 235));
            tail.BorderThickness = new Thickness(0, 1, 1, 0);
            tail.CornerRadius = new CornerRadius(2);
            tail.HorizontalAlignment = HorizontalAlignment.Left;
            tail.VerticalAlignment = VerticalAlignment.Top;
            tail.RenderTransform = new RotateTransform(-45, 7.5, 7.5);
            root.Children.Add(tail);

            bubble.HorizontalAlignment = HorizontalAlignment.Left;
            bubble.VerticalAlignment = VerticalAlignment.Top;
            bubble.Margin = new Thickness(12, 12, 0, 0);
            bubble.CornerRadius = new CornerRadius(20);
            bubble.Padding = new Thickness(18, 16, 18, 16);
            bubble.Background = Brushes.White;
            bubble.BorderBrush = new SolidColorBrush(Color.FromRgb(206, 222, 235));
            bubble.BorderThickness = new Thickness(1);
            bubble.Cursor = Cursors.Hand;
            bubble.ToolTip = "在这里与 DeepSeek 对话；右键可查看余额与设置";
            Grid content = new Grid();
            for (int row = 0; row < 8; row++)
                content.RowDefinitions.Add(new RowDefinition { Height = row == 6 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            bubble.Child = content;

            Grid masthead = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 12) };
            masthead.ColumnDefinitions.Add(new ColumnDefinition());
            masthead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            masthead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            brand.Text = "小鲸鱼";
            brand.FontWeight = FontWeights.SemiBold;
            brand.VerticalAlignment = VerticalAlignment.Center;
            brand.Foreground = new SolidColorBrush(Color.FromRgb(29, 71, 104));
            masthead.Children.Add(brand);
            refreshButton.Content = "↻";
            refreshButton.Width = 32;
            refreshButton.Height = 32;
            refreshButton.FontSize = 20;
            refreshButton.ToolTip = "立即刷新余额";
            StyleRoundedButton(refreshButton, Color.FromRgb(226, 245, 255),
                Color.FromRgb(39, 115, 168), 13);
            refreshButton.Click += async (s, e) => await RefreshBalanceAsync();
            Grid.SetColumn(refreshButton, 1);
            masthead.Children.Add(refreshButton);
            menuButton.Content = "⋯";
            menuButton.Width = 32;
            menuButton.Height = 32;
            menuButton.FontSize = 20;
            menuButton.ToolTip = "文字大小、缩放与设置";
            StyleRoundedButton(menuButton, Color.FromRgb(242, 247, 251), Color.FromRgb(58, 88, 112), 10);
            menuButton.Click += (s, e) => {
                widgetMenu.PlacementTarget = menuButton;
                widgetMenu.IsOpen = true;
            };
            Grid.SetColumn(menuButton, 2);
            masthead.Children.Add(menuButton);
            content.Children.Add(masthead);

            titleText.FontFamily = new FontFamily("Microsoft YaHei UI");
            titleText.FontSize = 11.5;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.Foreground = new SolidColorBrush(Color.FromRgb(91, 120, 147));
            titleText.Visibility = Visibility.Visible;
            Grid.SetRow(titleText, 1);
            content.Children.Add(titleText);

            amountText.FontFamily = new FontFamily("Microsoft YaHei UI");
            amountText.FontSize = 32;
            amountText.FontWeight = FontWeights.Bold;
            amountText.Foreground = new SolidColorBrush(Color.FromRgb(23, 72, 117));
            amountText.Margin = new Thickness(0, 2, 0, 4);
            amountText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(amountText, 2);
            content.Children.Add(amountText);

            detailText.FontFamily = new FontFamily("Microsoft YaHei UI");
            detailText.FontSize = 11;
            detailText.LineHeight = 17;
            detailText.TextWrapping = TextWrapping.Wrap;
            detailText.Foreground = new SolidColorBrush(Color.FromRgb(64, 89, 114));
            Grid.SetRow(detailText, 3);
            content.Children.Add(detailText);

            Border divider = new Border {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(219, 236, 247)),
                Margin = new Thickness(0, 12, 0, 8)
            };
            Grid.SetRow(divider, 4);
            content.Children.Add(divider);

            footerText.FontFamily = new FontFamily("Microsoft YaHei UI");
            footerText.FontSize = 10.5;
            footerText.TextWrapping = TextWrapping.Wrap;
            footerText.Foreground = new SolidColorBrush(Color.FromRgb(69, 111, 143));
            statusFrame.Background = new SolidColorBrush(Color.FromRgb(231, 247, 254));
            statusFrame.CornerRadius = new CornerRadius(9);
            statusFrame.Padding = new Thickness(8, 6, 8, 6);
            statusFrame.MinHeight = 30;
            footerText.MaxHeight = 48;
            footerText.TextTrimming = TextTrimming.CharacterEllipsis;
            statusFrame.Child = footerText;
            Grid.SetRow(statusFrame, 5);
            content.Children.Add(statusFrame);
            Border chatPanel = new Border {
                MinHeight = 48,
                CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 238, 248)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 10, 0, 0)
            };
            chatScroll.VerticalAlignment = VerticalAlignment.Stretch;
            chatScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            chatScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            chatScroll.Content = chatMessages;
            chatPanel.Child = chatScroll;
            Grid.SetRow(chatPanel, 6);
            // Conversation is displayed beside the pet, never inside the balance panel.
            RenderChatHistory("我在这里，今天也陪着你。", false);

            Grid inputRow = petInput;
            inputRow.ColumnDefinitions.Add(new ColumnDefinition());
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            chatInputFrame.Height = 40;
            chatInputFrame.CornerRadius = new CornerRadius(12);
            chatInputFrame.Background = new SolidColorBrush(Color.FromRgb(246, 249, 252));
            chatInputFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 220, 236));
            chatInputFrame.BorderThickness = new Thickness(1);
            chatInputFrame.Padding = new Thickness(8, 2, 8, 2);
            Grid inputInner = new Grid();
            chatInputFrame.Child = inputInner;
            chatInput.MaxLength = 4000;
            chatInput.BorderThickness = new Thickness(0);
            chatInput.Background = new SolidColorBrush(Color.FromRgb(246, 249, 252));
            chatInput.Foreground = new SolidColorBrush(Color.FromRgb(33, 55, 76));
            chatInput.VerticalContentAlignment = VerticalAlignment.Center;
            chatInput.Padding = new Thickness(1);
            chatInput.FontSize = 11.5;
            chatInput.ToolTip = "输入问题，按 Enter 发送（最多 4000 字）";
            chatInput.KeyDown += async (s, e) => {
                if (e.Key == Key.Enter) { e.Handled = true; await SendChatAsync(); }
            };
            chatInput.TextChanged += (s, e) => UpdateInputHint();
            chatInput.GotFocus += (s, e) => { UpdateInputHint(); chatInputFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 130, 193)); };
            chatInput.LostFocus += (s, e) => { UpdateInputHint(); chatInputFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 220, 236)); };
            inputInner.Children.Add(chatInput);
            inputHint.Text = "和小鲸鱼说点什么…";
            inputHint.FontSize = 11.5;
            inputHint.Foreground = new SolidColorBrush(Color.FromRgb(100, 121, 140));
            inputHint.VerticalAlignment = VerticalAlignment.Center;
            inputHint.Margin = new Thickness(3, 0, 0, 0);
            inputHint.IsHitTestVisible = false;
            inputInner.Children.Add(inputHint);
            inputRow.Children.Add(chatInputFrame);
            sendButton.Content = "↑";
            sendButton.Height = 40;
            sendButton.Margin = new Thickness(6, 0, 0, 0);
            sendButton.FontSize = 19;
            sendButton.ToolTip = "发送消息";
            sendButton.FontWeight = FontWeights.SemiBold;
            StyleRoundedButton(sendButton, Color.FromRgb(47, 133, 190), Colors.White, 12);
            sendButton.Click += async (s, e) => await SendChatAsync();
            Grid.SetColumn(sendButton, 1);
            inputRow.Children.Add(sendButton);
            root.Children.Add(bubble);
            petInput.HorizontalAlignment = HorizontalAlignment.Right;
            petInput.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(petInput);
            BuildSpeechVisual();
            root.Children.Add(chargeLayer);

            mascot.MouseLeftButtonDown += OnPointerDown;
            mascot.MouseMove += OnMascotMove;
            mascot.MouseLeftButtonUp += OnPointerUp;
            mascot.MouseLeave += (s, e) => AnimateFollow(0, 0);
            mascot.PreviewMouseWheel += OnScaleWheel;
            bubble.MouseLeftButtonDown += OnPointerDown;
            bubble.MouseMove += OnBubbleMove;
            bubble.MouseLeftButtonUp += OnPointerUp;
            bubble.PreviewMouseWheel += (s, e) => {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) OnScaleWheel(s, e);
            };

            UpdateLayoutMetrics();
            UpdateBubble();
        }

        private static void StyleRoundedButton(Button button, Color background, Color foreground,
            double radius)
        {
            button.Background = new SolidColorBrush(background);
            button.Foreground = new SolidColorBrush(foreground);
            button.BorderThickness = new Thickness(0);
            button.Cursor = Cursors.Hand;
            FrameworkElementFactory chrome = new FrameworkElementFactory(typeof(Border));
            chrome.SetValue(Border.BackgroundProperty, new SolidColorBrush(background));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            FrameworkElementFactory label = new FrameworkElementFactory(typeof(ContentPresenter));
            label.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            label.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(label);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            button.MouseEnter += (s, e) => { if (button.IsEnabled) button.Opacity = 0.82; };
            button.MouseLeave += (s, e) => button.Opacity = button.IsEnabled ? 1 : 0.5;
            button.IsEnabledChanged += (s, e) => button.Opacity = button.IsEnabled ? 1 : 0.5;
        }

        private void UpdateInputHint()
        {
            inputHint.Visibility = String.IsNullOrEmpty(chatInput.Text) &&
                !chatInput.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderChatHistory(string extra, bool pendingUser, bool warning = false)
        {
            string reply = extra;
            if (pendingUser) reply = "让我想一想…";
            else if (String.IsNullOrEmpty(reply) && chatHistory.Count > 0)
                reply = chatHistory[chatHistory.Count - 1].Content;
            if (pendingUser) SetSpeechContext(extra);
            else if (String.IsNullOrEmpty(extra) && chatHistory.Count>=2) SetSpeechContext(chatHistory[chatHistory.Count-2].Content);
            else SetSpeechContext(null);
            if (!String.IsNullOrEmpty(reply)) ShowSpeech(reply, !pendingUser && !warning, warning);
            speechCaption.Text = pendingUser ? "小鲸鱼 · 正在想" : warning ? "小鲸鱼 · 提醒你" : "小鲸鱼 · 对你说";
        }

        private void SetSpeechContext(string message)
        {
            speechContext.Text=String.IsNullOrEmpty(message)?"":"你："+message.Replace("\r"," ").Replace("\n"," ");
            speechContext.ToolTip=message;
            speechContext.Visibility=String.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        }

        private void BuildSpeechVisual()
        {
            speech.HorizontalAlignment = HorizontalAlignment.Right;
            speech.VerticalAlignment = VerticalAlignment.Top;
            speech.Background = Brushes.White;
            speech.BorderBrush = new SolidColorBrush(Color.FromRgb(202, 223, 239));
            speech.BorderThickness = new Thickness(1);
            speech.CornerRadius = new CornerRadius(19);
            speech.Padding = new Thickness(15, 10, 15, 12);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new DockPanel { Margin = new Thickness(0,0,0,6) };
            var close = new Button { Content = "×", Width = 27, Height = 26, ToolTip = "收起回复（右键可重新显示）" };
            StyleRoundedButton(close, Color.FromRgb(242,247,251), Color.FromRgb(64,99,126), 8);
            close.Click += (s,e) => { CompleteSpeech(); speech.Visibility = Visibility.Collapsed; RelayoutPet(); };
            DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            var complete = new Button { Content = "全文", Width = 42, Height = 26, Margin = new Thickness(0,0,6,0), ToolTip = "立即显示完整回复" };
            StyleRoundedButton(complete, Color.FromRgb(242,247,251), Color.FromRgb(64,99,126), 8);
            complete.Click += (s,e) => CompleteSpeech();
            DockPanel.SetDock(complete, Dock.Right); heading.Children.Add(complete);
            speechCaption.Text="小鲸鱼 · 陪着你";
            speechCaption.FontSize=13; speechCaption.FontWeight=FontWeights.SemiBold;
            speechCaption.VerticalAlignment=VerticalAlignment.Center;
            speechCaption.Foreground=new SolidColorBrush(Color.FromRgb(38,112,165));
            heading.Children.Add(speechCaption);
            layout.Children.Add(heading);
            speechContext.FontSize=13;
            speechContext.Foreground=new SolidColorBrush(Color.FromRgb(94,114,135));
            speechContext.TextTrimming=TextTrimming.CharacterEllipsis;
            speechContext.Margin=new Thickness(0,0,0,8);
            Grid.SetRow(speechContext,1); layout.Children.Add(speechContext);
            speechText.IsReadOnly = true;
            speechText.IsReadOnlyCaretVisible = false;
            speechText.TextWrapping = TextWrapping.Wrap;
            speechText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            speechText.BorderThickness = new Thickness(0);
            speechText.Background = Brushes.White;
            speechText.Padding = new Thickness(0);
            speechText.Foreground = new SolidColorBrush(Color.FromRgb(33,55,76));
            Grid.SetRow(speechText, 2); layout.Children.Add(speechText);
            speech.Child = layout;
            speech.RenderTransform = speechFollow;
            scene.Children.Add(speech);
            speechTimer.Interval = TimeSpan.FromMilliseconds(25);
            speechTimer.Tick += (s,e) => {
                speechProgress = Math.Min(speechOffsets.Length, speechProgress + 2);
                speechText.Text = speechProgress >= speechOffsets.Length ? fullSpeech : fullSpeech.Substring(0,speechOffsets[speechProgress]);
                if (speechProgress >= speechOffsets.Length) speechTimer.Stop();
            };
        }

        private void CompleteSpeech()
        {
            speechTimer.Stop();
            speechText.Text = fullSpeech;
        }

        private void ShowSpeech(string message, bool animate, bool warning)
        {
            speechTimer.Stop();
            fullSpeech = message;
            speechOffsets = StringInfo.ParseCombiningCharacters(message);
            speechProgress = 0;
            speechText.Foreground = new SolidColorBrush(warning ? Color.FromRgb(137,78,24) : Color.FromRgb(33,55,76));
            speechText.Text = animate ? "" : message;
            speech.Visibility = Visibility.Visible;
            if (speech.Child != null) RelayoutPet();
            if (animate) speechTimer.Start();
            speechCaption.Text=warning?"小鲸鱼 · 提醒你":"小鲸鱼 · 对你说";
            speechText.ScrollToHome();
        }

        private void RelayoutPet()
        {
            double right = Left + Width, bottom = Top + Height;
            UpdateLayoutMetrics();
            if (!Double.IsNaN(right) && !Double.IsNaN(bottom))
            {
                Left = right - Width; Top = bottom - Height;
                SnapToWorkArea(false);
            }
        }

        private void ToggleDataPanel()
        {
            settings.HideDataPanel = !settings.HideDataPanel;
            if (dataToggle != null) dataToggle.IsChecked = !settings.HideDataPanel;
            if (trayDataToggle != null) trayDataToggle.Checked = !settings.HideDataPanel;
            RelayoutPet();
            try { SaveState(); } catch (Exception) { }
        }

        private void AddChatCard(string speaker, string message, bool fromUser, bool warning)
        {
            Border card = new Border {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(warning ? Color.FromRgb(255, 244, 230) :
                    fromUser ? Color.FromRgb(216, 241, 253) : Colors.White),
                BorderBrush = new SolidColorBrush(warning ? Color.FromRgb(242, 210, 170) :
                    Color.FromRgb(221, 236, 246)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Padding = fromUser || warning ? new Thickness(10, 7, 10, 7) : new Thickness(0, 3, 0, 4),
                Margin = new Thickness(0, 0, 0, 9)
            };
            if (!warning) card.Background = fromUser ? new SolidColorBrush(Color.FromRgb(240, 246, 251)) : Brushes.Transparent;
            TextBlock text = new TextBlock {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = bodyTextSize,
                LineHeight = Math.Round(bodyTextSize * 1.6),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 55, 76))
            };
            text.Inlines.Add(new System.Windows.Documents.Run(speaker + "  ") {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(fromUser ? Color.FromRgb(39, 112, 160) :
                    Color.FromRgb(46, 132, 173))
            });
            text.Inlines.Add(new System.Windows.Documents.Run(message));
            card.Child = text;
            chatMessages.Children.Add(card);
        }

        private static BitmapSource LoadMascot()
        {
            try
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WhaleMascot");
                if (stream == null)
                {
                    string file = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "海洋少女立绘.png");
                    if (File.Exists(file)) stream = File.OpenRead(file);
                }
                if (stream == null) return null;
                using (stream)
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception) { return null; }
        }

        private void BuildMenus()
        {
            AddCompanionToggle("安静陪伴 · 减少小动作",()=>settings.QuietMode,()=> {settings.QuietMode=!settings.QuietMode;queuedAction="";});
            AddCompanionToggle("让她小睡 / 唤醒",()=>sleeping,()=>SetSleeping(!sleeping));
            AddCompanionToggle("锁定桌宠位置",()=>settings.LockPosition,()=>settings.LockPosition=!settings.LockPosition);
            AddCompanionToggle("显示聊天输入栏",()=>!settings.HideInput,()=>{settings.HideInput=!settings.HideInput;RelayoutPet();});
            var fpsMenu=new MenuItem {Header="动画帧率"};
            var fpsTray=new Forms.ToolStripMenuItem("动画帧率");
            foreach(int fps in new[]{30,60,120}) {
                int rate=fps;
                string label=rate+" 帧"+(rate==30?" · 节能":rate==60?" · 均衡":" · 高刷新");
                var item=new MenuItem {Header=label,IsCheckable=true};
                var trayItem=new Forms.ToolStripMenuItem(label);
                Action choose=()=>{settings.TargetFps=rate;nextFrameAt=0;RefreshCompanionChecks();try{SaveState();}catch(Exception){}};
                item.Click+=(s,e)=>choose();trayItem.Click+=(s,e)=>Dispatcher.BeginInvoke(choose);
                companionChecks.Add(()=>{item.IsChecked=settings.TargetFps==rate;trayItem.Checked=item.IsChecked;});
                fpsMenu.Items.Add(item);fpsTray.DropDownItems.Add(trayItem);
            }
            widgetMenu.Items.Add(fpsMenu);trayMenu.Items.Add(fpsTray);RefreshCompanionChecks();
            var poseMenu = new MenuItem { Header = "鲸鱼娘的姿态" };
            var trayPoseMenu = new Forms.ToolStripMenuItem("鲸鱼娘的姿态");
            string[] ids = { "auto", "stand", "sit", "prone" };
            string[] labels = { "随性换姿势（约 60–85 秒）", "站着 · 招呼 / 轻跳", "坐着 · 歪头 / 摇摆", "趴着 · 伸懒腰 / 小憩" };
            for (int i=0;i<ids.Length;i++) {
                string id=ids[i];
                var item=new MenuItem { Header=labels[i],Tag=id,IsCheckable=true };
                item.Click+=(s,e)=>ChoosePose(id); poseMenu.Items.Add(item); poseMenuItems.Add(item);
                var trayItem=new Forms.ToolStripMenuItem(labels[i]) { Tag=id };
                trayItem.Click+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>ChoosePose(id)));
                trayPoseMenu.DropDownItems.Add(trayItem); trayPoseMenuItems.Add(trayItem);
            }
            widgetMenu.Items.Add(poseMenu); trayMenu.Items.Add(trayPoseMenu); UpdatePoseChecks();
            dataToggle = new MenuItem { Header = "显示左侧数据栏", IsCheckable = true, IsChecked = !settings.HideDataPanel };
            dataToggle.Click += (s,e) => ToggleDataPanel();
            widgetMenu.Items.Add(dataToggle);
            trayDataToggle = new Forms.ToolStripMenuItem("显示左侧数据栏") { Checked = !settings.HideDataPanel };
            trayDataToggle.Click += (s,e) => Dispatcher.BeginInvoke(new Action(ToggleDataPanel));
            trayMenu.Items.Add(trayDataToggle);
            AddMenuItem("显示上次回复", () => { speech.Visibility = Visibility.Visible; CompleteSpeech(); RelayoutPet(); });
            AddMenuItem("立即刷新", async () => await RefreshBalanceAsync());
            var textMenu = new MenuItem { Header = "文字大小" };
            var trayTextMenu = new Forms.ToolStripMenuItem("文字大小");
            foreach (int size in new[] { 13, 14, 16, 18 })
            {
                int selectedSize = size;
                string label = size + (size == 14 ? " · 默认" : size == 13 ? " · 紧凑" : size == 16 ? " · 大字" : " · 特大");
                var item = new MenuItem { Header = label, Tag = size, IsCheckable = true };
                item.Click += (s, e) => ApplyTextSize(selectedSize);
                textMenu.Items.Add(item);
                textSizeItems.Add(item);
                var trayItem = new Forms.ToolStripMenuItem(label) { Tag = size };
                trayItem.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => ApplyTextSize(selectedSize)));
                trayTextMenu.DropDownItems.Add(trayItem);
                trayTextSizeItems.Add(trayItem);
            }
            widgetMenu.Items.Add(textMenu);
            trayMenu.Items.Add(trayTextMenu);
            UpdateTextSizeChecks();
            AddMenuItem("角色放大 10%", () => ApplyScale(settings.ScalePercent + 10));
            AddMenuItem("角色缩小 10%", () => ApplyScale(settings.ScalePercent - 10));
            AddMenuItem("恢复 100% 大小", () => ApplyScale(100));
            AddMenuItem("清空本次对话", () => {
                chatEpoch++;
                chatHistory.Clear();
                RenderChatHistory("对话已清空。", false);
            });
            AddMenuItem("余额明细", () => ShowDetails());
            AddMenuItem("近 7 天观测记录", () => ShowHistory());
            AddMenuItem("设置 API Key 与预警", () => ShowSettings());
            AddMenuItem("隐藏小鲸鱼", () => HideWidget());
            widgetMenu.Items.Add(new Separator());
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            AddMenuItem("退出", () => Close());
            mascot.ContextMenu = widgetMenu;
            bubble.ContextMenu = widgetMenu;
            petInput.ContextMenu = widgetMenu;
        }

        private void AddMenuItem(string label, Action action)
        {
            MenuItem item = new MenuItem { Header = label };
            item.Click += (s, e) => action();
            widgetMenu.Items.Add(item);
            Forms.ToolStripMenuItem trayItem = new Forms.ToolStripMenuItem(label);
            trayItem.Click += (s, e) => Dispatcher.BeginInvoke(action);
            trayMenu.Items.Add(trayItem);
        }

        private void AddCompanionToggle(string label,Func<bool> read,Action change)
        {
            var item=new MenuItem {Header=label,IsCheckable=true};
            var trayItem=new Forms.ToolStripMenuItem(label);
            Action run=()=> {change();RefreshCompanionChecks();try{SaveState();}catch(Exception){}};
            item.Click+=(s,e)=>run();trayItem.Click+=(s,e)=>Dispatcher.BeginInvoke(run);
            companionChecks.Add(()=>{item.IsChecked=read();trayItem.Checked=item.IsChecked;});
            widgetMenu.Items.Add(item);trayMenu.Items.Add(trayItem);
        }
        private void RefreshCompanionChecks() {foreach(var update in companionChecks)update();}
        private void SetSleeping(bool value)
        {
            sleeping=value;activeAction="";queuedAction="";nextAmbient=animationClock.Elapsed.TotalSeconds+25;
            if(value) pendingPose="prone";
            else pendingPose=settings.AutoPose?"sit":settings.PetPose;
            RefreshCompanionChecks();
            if(!chatBusy) {SetSpeechContext(null);ShowSpeech(value?"我趴着眯一会儿。余额我会继续帮你留意，轻轻点我就能叫醒我。":"唔…醒啦！我在听，你说吧。",true,false);}
        }

        private void BuildTray()
        {
            try { ownedIcon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { ownedIcon = null; }
            tray.Icon = ownedIcon ?? Drawing.SystemIcons.Application;
            tray.Text = "DeepSeek 小鲸鱼独立版";
            tray.ContextMenuStrip = trayMenu;
            tray.Visible = true;
            tray.DoubleClick += (s, e) => Dispatcher.BeginInvoke(new Action(ShowWidget));
        }

        private void PlaceInitialWindow()
        {
            if (settings.WindowX != Int32.MinValue && settings.WindowY != Int32.MinValue)
            {
                Left = settings.WindowX;
                Top = settings.WindowY;
            }
            else
            {
                Rect area = SystemParameters.WorkArea;
                Left = area.Right - Width - 12;
                Top = area.Bottom - Height - 8;
            }
            SnapToWorkArea(false);
        }

        private void SaveState()
        {
            if(offlinePreview)return;
            settings.WindowX = (int)Math.Round(Left);
            settings.WindowY = (int)Math.Round(Top);
            ledger.WriteTo(settings);
            SettingsStore.Save(settings);
        }

        private void ShowSettings()
        {
            if (busy)
            {
                MessageBox.Show(this, "请等待本次查询完成后再修改设置。", "正在查询",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SettingsWindow dialog = new SettingsWindow(sessionKey,
                !String.IsNullOrEmpty(settings.ProtectedKey), settings.AlertThreshold,
                settings.BudgetThreshold, settings.RefreshSeconds, settings.ScalePercent) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            bool changedKey = !String.Equals(sessionKey, dialog.ApiKey, StringComparison.Ordinal);
            sessionKey = dialog.ApiKey;
            if (changedKey)
            {
                chatEpoch++;
                chatHistory.Clear();
                RenderChatHistory("已切换 API Key，本次对话已清空。", false);
                ledger.Reset();
                latest = null;
                lastUpdated = DateTime.MinValue;
                alertActive = false;
                lastBudgetAlertDay = DateTime.MinValue;
                bubblePage = 0;
            }
            settings.AlertThreshold = dialog.AlertThreshold;
            settings.BudgetThreshold = dialog.BudgetThreshold;
            settings.RefreshSeconds = dialog.RefreshSeconds;
            refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
            ApplyScale(dialog.ScalePercent);
            try
            {
                settings.ProtectedKey = dialog.RememberKey ? SettingsStore.ProtectKey(sessionKey) : "";
                SaveState();
                status = "设置已保存";
            }
            catch (Exception)
            {
                settings.ProtectedKey = "";
                status = "设置未保存，本次仍可查询";
                MessageBox.Show(this, "本地设置保存失败，请检查文件权限。", "保存失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateBubble();
            if (IsVisible) Dispatcher.BeginInvoke(new Action(async () => await RefreshBalanceAsync()));
        }

        private void HideWidget()
        {
            Hide();
            tray.ShowBalloonTip(2500, "DeepSeek 小鲸鱼", "已隐藏，双击托盘图标可重新显示。",
                Forms.ToolTipIcon.Info);
        }

        private void ShowWidget()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
        }

        private async Task RefreshBalanceAsync()
        {
            if (busy) return;
            if (String.IsNullOrWhiteSpace(sessionKey))
            {
                status = "右键设置 DeepSeek API Key";
                UpdateBubble();
                return;
            }
            busy = true;
            status = "正在向 DeepSeek 查询…";
            UpdateBubble();
            try
            {
                BalanceResult previous = latest;
                BalanceResult result = await client.QueryAsync(sessionKey);
                latest = result;
                lastUpdated = DateTime.Now;
                ledger.Observe(result, lastUpdated);
                string decrease = DescribeDecrease(previous, result);
                status = decrease ?? (result.IsAvailable ? "账户可用" : "余额不足或账户不可用");
                try { SaveState(); }
                catch (Exception) { status += " · 记录未保存"; }
                CheckAlerts();
                if (decrease != null)
                {
                    bubblePage = 0;
                    PlayClip("charge.gif", false);
                    foreach (BalanceDrop drop in BalanceDrop.Between(previous,result)) ShowCharge(drop);
                    tray.ShowBalloonTip(6000, "DeepSeek 余额变动", decrease, Forms.ToolTipIcon.Info);
                }
            }
            catch (BalanceQueryException ex)
            {
                status = (latest == null ? "" : "显示上次余额 · ") + ex.Message;
            }
            catch (Exception)
            {
                status = latest == null ? "查询失败，请稍后重试" : "显示上次余额 · 查询失败";
            }
            finally
            {
                busy = false;
                UpdateBubble();
            }
        }

        private static string DescribeDecrease(BalanceResult before, BalanceResult after)
        {
            if (before == null || before.Items == null || after == null || after.Items == null) return null;
            List<string> changes = new List<string>();
            foreach (BalanceInfo current in after.Items)
            {
                foreach (BalanceInfo old in before.Items)
                {
                    if (!String.Equals(current.Currency, old.Currency, StringComparison.OrdinalIgnoreCase)) continue;
                    decimal a, b;
                    if (Decimal.TryParse(old.Total, NumberStyles.Number, CultureInfo.InvariantCulture, out a) &&
                        Decimal.TryParse(current.Total, NumberStyles.Number, CultureInfo.InvariantCulture, out b) && a > b)
                        changes.Add(Money(current.Currency, (a - b).ToString("0.00########", CultureInfo.InvariantCulture)));
                    break;
                }
            }
            return changes.Count == 0 ? null :
                "检测到扣费约 " + String.Join("、", changes) + "（与上次余额相比）";
        }

        private async Task SendChatAsync()
        {
            if (chatBusy) return;
            string message = chatInput.Text.Trim();
            if (message.Length == 0) return;
            if(sleeping)SetSleeping(false);
            if (String.IsNullOrWhiteSpace(sessionKey))
            {
                RenderChatHistory("请先右键打开设置，输入 DeepSeek API Key。", false, true);
                return;
            }
            List<ChatTurn> request = new List<ChatTurn>(chatHistory);
            request.Insert(0, new ChatTurn("system", "你是用户桌面上的小鲸鱼娘，一个温柔自然的桌宠助手。用纯文字直接对用户说话，默认简短回复两到四句；用户需要详细回答时再展开。避免 Markdown 标题、表格和舞台动作描述。不要编造账户余额、扣费记录或声称执行了未执行的操作。"));
            request.Add(new ChatTurn("user", message));
            int requestEpoch = chatEpoch;
            chatBusy = true;
            sendButton.IsEnabled = false;
            chatInput.IsEnabled = false;
            RenderChatHistory(message, true);
            PlayClip("tilt.gif", false);
            try
            {
                string reply = await chatClient.SendAsync(sessionKey, request);
                if (requestEpoch != chatEpoch) return;
                chatHistory.Add(new ChatTurn("user", message));
                chatHistory.Add(new ChatTurn("assistant", reply));
                while (chatHistory.Count > 12) chatHistory.RemoveAt(0);
                chatInput.Clear();
                RenderChatHistory(null, false);
                PlayClip("hop.gif", false);
            }
            catch (ChatQueryException ex)
            {
                if (requestEpoch == chatEpoch)
                    RenderChatHistory("发送失败：" + ex.Message + " 输入内容已保留，可重试。",
                        false, true);
            }
            catch (Exception)
            {
                if (requestEpoch == chatEpoch)
                    RenderChatHistory("对话暂时失败，请稍后重试。输入内容已保留。",
                        false, true);
            }
            finally
            {
                chatBusy = false;
                sendButton.IsEnabled = true;
                chatInput.IsEnabled = true;
                chatInput.Focus();
            }
        }

        private BalanceInfo MainBalance()
        {
            if (latest == null || latest.Items == null || latest.Items.Count == 0) return null;
            foreach (BalanceInfo item in latest.Items)
                if (String.Equals(item.Currency, "CNY", StringComparison.OrdinalIgnoreCase)) return item;
            return latest.Items[0];
        }

        private static string Money(string currency, string value)
        {
            string symbol = String.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥" :
                String.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$" : currency;
            return symbol + " " + value;
        }

        private void UpdateBubble()
        {
            BalanceInfo main = MainBalance();
            if (bubblePage == 1 && main != null)
            {
                titleText.Text = "余额组成  /  " + main.Currency;
                amountText.Text = Money(main.Currency, main.Total);
                amountText.FontSize = Math.Round(bodyTextSize * 2.25);
                detailText.Text = "赠送余额  " + Money(main.Currency, main.Granted) +
                    "\n充值余额  " + Money(main.Currency, main.ToppedUp);
                footerText.Text = "点击气泡查看近 7 天 · 右键看完整明细";
            }
            else if (bubblePage == 2)
            {
                titleText.Text = "近 7 天观测消耗";
                amountText.Text = "¥ " + SumLastDays("CNY").ToString("0.00", CultureInfo.InvariantCulture);
                amountText.FontSize = Math.Round(bodyTextSize * 2.25);
                detailText.Text = "今日  ¥ " + TodayUsed("CNY").ToString("0.00", CultureInfo.InvariantCulture) +
                    "\n昨日  ¥ " + ledger.UsedOn("CNY", DateTime.Today.AddDays(-1)).ToString("0.00", CultureInfo.InvariantCulture);
                footerText.Text = "由余额下降估算，不是官方账单";
            }
            else
            {
                titleText.Text = "当前可用余额";
                amountText.Text = main == null ? "还没查询哦" : Money(main.Currency, main.Total);
                amountText.FontSize = Math.Round(bodyTextSize * (main == null ? 1.65 : 2.25));
                detailText.Text = main == null ? "点击角色，或右键设置 API Key。" :
                    "今日观测消耗  " + Money(main.Currency,
                        TodayUsed(main.Currency).ToString("0.00", CultureInfo.InvariantCulture));
                footerText.Text = status + (lastUpdated == DateTime.MinValue ? "" :
                    "  ·  " + lastUpdated.ToString("HH:mm", CultureInfo.InvariantCulture) + " 更新");
            }
            bool charge = bubblePage == 0 && status.StartsWith("检测到扣费", StringComparison.Ordinal);
            statusFrame.Background = new SolidColorBrush(charge ?
                Color.FromRgb(255, 244, 230) : Color.FromRgb(242, 247, 251));
            footerText.Foreground = new SolidColorBrush(charge ?
                Color.FromRgb(130, 76, 30) : Color.FromRgb(62, 85, 106));
            footerText.ToolTip = footerText.Text;
        }

        private decimal TodayUsed(string currency)
        {
            return ledger.Day == DateTime.Today ? ledger.Used(currency) : 0m;
        }

        private decimal SumLastDays(string currency)
        {
            decimal total = 0m;
            for (int i = 0; i < 7; i++) total += ledger.UsedOn(currency, DateTime.Today.AddDays(-i));
            return total;
        }

        private void CheckAlerts()
        {
            BalanceInfo cny = null;
            if (latest != null && latest.Items != null)
                foreach (BalanceInfo item in latest.Items)
                    if (String.Equals(item.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
                    { cny = item; break; }
            decimal amount;
            if (cny != null && Decimal.TryParse(cny.Total, NumberStyles.Number,
                CultureInfo.InvariantCulture, out amount))
            {
                bool low = settings.AlertThreshold > 0m && amount < settings.AlertThreshold;
                if (low && !alertActive)
                {
                    tray.ShowBalloonTip(6000, "DeepSeek 余额预警",
                        "当前人民币余额 ¥" + amount.ToString("0.00", CultureInfo.InvariantCulture),
                        Forms.ToolTipIcon.Warning);
                    Pulse();
                }
                alertActive = low;
                if (settings.BudgetThreshold > 0m && ledger.Day == DateTime.Today &&
                    ledger.Used("CNY") >= settings.BudgetThreshold && lastBudgetAlertDay != DateTime.Today)
                {
                    lastBudgetAlertDay = DateTime.Today;
                    tray.ShowBalloonTip(6000, "DeepSeek 今日预算提醒",
                        "今日观测消耗已达 ¥" + ledger.Used("CNY").ToString("0.00", CultureInfo.InvariantCulture),
                        Forms.ToolTipIcon.Warning);
                    Pulse();
                }
            }
            else alertActive = false;
        }

        private void ShowDetails()
        {
            if (latest == null)
            {
                MessageBox.Show(this, "暂无余额数据，请先点击角色刷新。", "余额明细",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Window dialog;
            StackPanel body = NewInfoDialog("DeepSeek 余额明细", 540, 410, out dialog);
            AddInfoLine(body, latest.IsAvailable ? "账户可用" : "账户不可用", true);
            AddInfoLine(body, "更新于 " + lastUpdated.ToString("yyyy-MM-dd HH:mm:ss"), false);
            foreach (BalanceInfo item in latest.Items)
            {
                AddInfoLine(body, item.Currency + "  ·  可用总额 " + item.Total, true);
                AddInfoLine(body, "赠送 " + item.Granted + "   充值 " + item.ToppedUp +
                    "   今日观测消耗 " + TodayUsed(item.Currency).ToString("0.00", CultureInfo.InvariantCulture), false);
            }
            AddInfoLine(body, "今日观测消耗从当天首次查询起算；余额增加不抵消已记录的下降。", false);
            dialog.ShowDialog();
        }

        private void ShowHistory()
        {
            Window dialog;
            StackPanel body = NewInfoDialog("近 7 天观测记录", 480, 420, out dialog);
            AddInfoLine(body, "人民币合计 ¥ " + SumLastDays("CNY").ToString("0.00", CultureInfo.InvariantCulture), true);
            for (int i = 0; i < 7; i++)
            {
                DateTime day = DateTime.Today.AddDays(-i);
                AddInfoLine(body, day.ToString("MM 月 dd 日  dddd", new CultureInfo("zh-CN")) +
                    "    人民币 ¥ " + ledger.UsedOn("CNY", day).ToString("0.00", CultureInfo.InvariantCulture) +
                    "    美元 $ " + ledger.UsedOn("USD", day).ToString("0.00", CultureInfo.InvariantCulture), false);
            }
            AddInfoLine(body, "由余额下降量估算，不是 DeepSeek 官方账单。", false);
            dialog.ShowDialog();
        }

        private StackPanel NewInfoDialog(string title, double width, double height, out Window dialog)
        {
            dialog = new Window {
                Title = title, Owner = this, Width = width, Height = height,
                MinWidth = width - 40, MinHeight = height - 40,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White, ShowInTaskbar = false,
                FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13,
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            TextOptions.SetTextFormattingMode(dialog, TextFormattingMode.Display);
            Border frame = new Border { Padding = new Thickness(22) };
            dialog.Content = frame;
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            frame.Child = scroll;
            StackPanel body = new StackPanel();
            scroll.Content = body;
            return body;
        }

        private static void AddInfoLine(StackPanel body, string value, bool heading)
        {
            TextBlock text = new TextBlock {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, heading ? 14 : 3, 0, heading ? 5 : 9),
                FontSize = heading ? 16 : 12.5,
                FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(heading ? Color.FromRgb(27, 69, 112) : Color.FromRgb(70, 88, 108))
            };
            body.Children.Add(text);
        }

        private void OnPointerDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (ReferenceEquals(sender, bubble) && IsChatControl(e.OriginalSource as DependencyObject)) return;
            pressed = true;
            dragging = false;
            pressedSource = sender;
            Point touch=e.GetPosition(mascot);
            touchedHead=ReferenceEquals(sender,mascot) && (currentPose=="prone" ? touch.X<mascot.ActualWidth*.55&&touch.Y<mascot.ActualHeight*.73 : touch.Y<mascot.ActualHeight*.53);
            cursorStart = Forms.Control.MousePosition;
            lastDragCursor = cursorStart;
            Mouse.Capture((IInputElement)sender);
            e.Handled = true;
        }

        private bool IsChatControl(DependencyObject source)
        {
            while (source != null && !ReferenceEquals(source, bubble))
            {
                if (source is Button || source is TextBox || source is ScrollViewer ||
                    ReferenceEquals(source, chatInputFrame)) return true;
                source = source is Visual ? VisualTreeHelper.GetParent(source) :
                    LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        private void OnMascotMove(object sender, MouseEventArgs e)
        {
            if (pressed && ReferenceEquals(pressedSource, sender))
            {
                MoveIfDragging();
                return;
            }
            if (pressed) return;
            Point p = e.GetPosition(mascot);
            AnimateFollow((p.X / mascot.ActualWidth - 0.5) * 10,
                (p.Y / mascot.ActualHeight - 0.5) * 6);
        }

        private void OnBubbleMove(object sender, MouseEventArgs e)
        {
            if (pressed && ReferenceEquals(pressedSource, sender)) MoveIfDragging();
        }

        private void MoveIfDragging()
        {
            Drawing.Point now = Forms.Control.MousePosition;
            if (!dragging && Math.Abs(now.X - cursorStart.X) + Math.Abs(now.Y - cursorStart.Y) < 7) return;
            dragging = true;
            if(settings.LockPosition)return;
            int dx = now.X - lastDragCursor.X;
            int dy = now.Y - lastDragCursor.Y;
            lastDragCursor = now;
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix fromDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            Vector delta = fromDevice.Transform(new Vector(dx, dy));
            Left += delta.X;
            Top += delta.Y;
        }

        private async void OnPointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!pressed || e.ChangedButton != MouseButton.Left) return;
            Mouse.Capture(null);
            pressed = false;
            e.Handled = true;
            if (dragging)
            {
                dragging = false;
                if(!settings.LockPosition)SnapToWorkArea(true);
                try { SaveState(); } catch (Exception) { }
            }
            else if (ReferenceEquals(sender, mascot))
            {
                ReactToPetClick(animationClock.Elapsed.TotalSeconds);
                if(clickBurst==1) await RefreshBalanceAsync();
            }
            else
            {
                bubblePage = (bubblePage + 1) % 3;
                UpdateBubble();
            }
        }

        private void SnapToWorkArea(bool edgeSnap)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix fromDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            Forms.Screen screen = handle == IntPtr.Zero
                ? Forms.Screen.FromPoint(Forms.Control.MousePosition)
                : Forms.Screen.FromHandle(handle);
            Drawing.Rectangle pixels = screen.WorkingArea;
            Point a = fromDevice.Transform(new Point(pixels.Left, pixels.Top));
            Point b = fromDevice.Transform(new Point(pixels.Right, pixels.Bottom));
            double minX = a.X;
            double maxX = Math.Max(minX, b.X - Width);
            double minY = a.Y;
            double maxY = Math.Max(minY, b.Y - Height);
            double x = Math.Max(minX, Math.Min(Left, maxX));
            double y = Math.Max(minY, Math.Min(Top, maxY));
            if (edgeSnap)
            {
                if (Math.Abs(x - minX) < 24) x = minX;
                if (Math.Abs(x - maxX) < 24) x = maxX;
                if (Math.Abs(y - minY) < 24) y = minY;
                if (Math.Abs(y - maxY) < 24) y = maxY;
            }
            Left = x;
            Top = y;
        }

        private void AnimateFollow(double x, double y)
        {
            lookX=Math.Max(-1,Math.Min(1,x/5));lookY=Math.Max(-1,Math.Min(1,y/3));
        }

        private void ReactToPetClick(double now)
        {
            if(sleeping) {SetSleeping(false);clickBurst=0;return;}
            clickBurst=now-lastPetClick<.75 ? clickBurst+1 : 1;
            lastPetClick=now; nextAmbient=now+20;
            string text;
            if(clickBurst>=4) { PlayClip("annoyed",false); text=clickBurst%2==0?"好啦好啦，我看见你啦。轻一点戳嘛～":"已经在认真看你啦，给我喘口气嘛。"; }
            else if(clickBurst>=2) { PlayClip("shy",false); text=clickBurst==2?"唔…一直这样戳，我会不好意思的。":"脸都有点热了…你是不是故意的呀？"; }
            else {
                PlayClip(touchedHead?"pet":ClickClips[reactionIndex++%ClickClips.Length],false);
                text=touchedHead?"嗯…这样轻轻摸摸就很好。再陪我一会儿吧。":currentPose=="prone"?"嗯？让我再趴一小会儿，也有在听你说哦。":
                    currentPose=="stand"?"我在呢！是想叫我陪你一会儿吗？":"被你发现我在发呆了。有什么想和我说的呀？";
            }
            // Local reactions do not enter the API history or cancel a network reply.
            if(!chatBusy) { SetSpeechContext(null); ShowSpeech(text,true,false); }
        }

        private void Pulse()
        {
            PlayClip("charge",false);
        }

        private static BitmapSource LoadPose(string name)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "poses", name);
                if (!File.Exists(path)) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        }

        private void PlayClip(string fileName, bool loop)
        {
            string action = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if(sleeping && action!="charge")return;
            // Facial reactions can replace a gentle action immediately. Mesh velocity
            // is retained, so a burst never teleports the head back to neutral.
            if((action=="shy"||action=="annoyed") && activeAction!="hop" && animationClock.Elapsed.TotalSeconds-transitionStart>=.6)
            {activeAction=action;actionStart=animationClock.Elapsed.TotalSeconds;queuedAction="";return;}
            if (!String.IsNullOrEmpty(activeAction) || animationClock.Elapsed.TotalSeconds-transitionStart<.6)
                queuedAction = action;
            else { activeAction = action; actionStart = animationClock.Elapsed.TotalSeconds; }
        }

        private void UpdatePoseChecks()
        {
            foreach (var item in poseMenuItems) item.IsChecked = (string)item.Tag == (settings.AutoPose ? "auto" : currentPose);
            foreach (var item in trayPoseMenuItems) item.Checked = (string)item.Tag == (settings.AutoPose ? "auto" : currentPose);
        }

        private void ChoosePose(string pose)
        {
            sleeping=false;RefreshCompanionChecks();
            settings.AutoPose = pose == "auto";
            if(settings.AutoPose) pendingPose="";
            if (!settings.AutoPose) pendingPose = pose;
            lastPoseChange = animationClock.Elapsed.TotalSeconds;
            UpdatePoseChecks();
            if(!settings.AutoPose) settings.PetPose=pose;
            try { SaveState(); } catch (Exception) { }
        }

        private bool ChangePose(string pose, double now)
        {
            if (!poseImages.ContainsKey(pose) || pose == currentPose) return false;
            departingMascot.Source = mascot.Source;
            departingMascot.Apply(currentPose,displayedMotion);
            departingMascot.RenderTransformOrigin = mascot.RenderTransformOrigin;
            departingMascot.RenderTransform = mascot.RenderTransform.CloneCurrentValue();
            oldSpeechOffset = speechFollow.Y;
            currentPose = pose;
            if(!sleeping) settings.PetPose = pose;
            eyesOpen = poseImages[pose][0]; eyesClosed = poseImages[pose][1];
            mascot.Source = eyesOpen;
            transitionStart = now; lastPoseChange = now;
            UpdateHeadAnchor(); UpdatePoseChecks();
            return true;
        }

        private void UpdateHeadAnchor()
        {
            headX = currentPose == "prone" ? .30 : .50;
            headY = currentPose == "prone" ? .24 : .035;
            mascot.RenderTransformOrigin = new Point(.5,.94);
        }

        private void ShowCharge(BalanceDrop drop)
        {
            if (charges.Count >= 4) { chargeLayer.Children.Remove(charges[0].Visual); charges.RemoveAt(0); }
            var visual = new FloatingAmount(drop.FloatingText, Math.Max(26,bodyTextSize*1.8));
            var particle = new ChargeParticle { Visual=visual, Start=animationClock.Elapsed.TotalSeconds, Lane=charges.Count };
            charges.Add(particle); chargeLayer.Children.Add(visual);
            UpdateCharges(animationClock.Elapsed.TotalSeconds);
        }

        private void UpdateCharges(double now)
        {
            if(charges.Count==0) return;
            double left=Width-mascot.Margin.Right-mascot.Width;
            double top=Height-mascot.Margin.Bottom-mascot.Height;
            foreach (var p in new List<ChargeParticle>(charges))
            {
                double t=Math.Max(0,now-p.Start);
                if(t>=2.2) { chargeLayer.Children.Remove(p.Visual); charges.Remove(p); continue; }
                double rise=46*(1-Math.Exp(-1.8*t));
                double x=left+mascot.Width*(.5+(headX-.5)*idleBreath.ScaleX)-p.Visual.Width/2+p.Lane*18;
                double y=top+mascot.Height*(.94+(headY-.94)*idleBreath.ScaleY)-p.Visual.Height-rise-p.Lane*18;
                Canvas.SetLeft(p.Visual,Math.Max(4,Math.Min(Width-p.Visual.Width-4,x)));
                Canvas.SetTop(p.Visual,Math.Max(4,y));
                p.Visual.Opacity=t<.12 ? t/.12 : t>1.4 ? Math.Max(0,(2.2-t)/.8) : 1;
            }
        }

        private void RenderPet(double now)
        {
            double dt=lastMotionTime<0?1.0/60:Math.Max(0,Math.Min(.05,now-lastMotionTime));
            lastMotionTime=now;
            double transition=now-transitionStart;
            if(!String.IsNullOrEmpty(activeAction) && now-actionStart>=PetMotionEngine.Duration(currentPose,activeAction))
                activeAction="";
            if(String.IsNullOrEmpty(activeAction) && transition>=.6)
            {
                if(!String.IsNullOrEmpty(pendingPose)) { if(ChangePose(pendingPose,now)) transition=0; pendingPose=""; }
                else if(!sleeping && !settings.QuietMode && settings.AutoPose && now-lastPoseChange>=60+(int)(lastPoseChange*7)%25 && !chatBusy && !dragging && !pressed && !chatInput.IsKeyboardFocused)
                { if(ChangePose(currentPose=="sit"?"stand":currentPose=="stand"?"prone":"sit",now)) transition=0; }
                else if(!String.IsNullOrEmpty(queuedAction)) { activeAction=queuedAction; queuedAction=""; actionStart=now; }
                else if(!sleeping && !settings.QuietMode && now>=nextAmbient && !chatBusy && !dragging && !pressed && !chatInput.IsKeyboardFocused)
                { activeAction=currentPose=="prone"?"twirl":"tilt"; actionStart=now; nextAmbient=now+18+(int)now%9; }
            }
            var motion=PetMotionEngine.Sample(currentPose,sleeping?"sleep":activeAction,now-actionStart,now);
            if(sleeping && activeAction=="charge") {
                var reaction=PetMotionEngine.Sample(currentPose,"charge",now-actionStart,now);
                motion.HeadY+=reaction.HeadY*.5;motion.Limb+=reaction.Limb*.5;
            }
            double lx=softLookX.Step(sleeping?0:lookX,dt,8),ly=softLookY.Step(sleeping?0:lookY,dt,8);
            double strength=settings.QuietMode?.45:1;
            motion.HeadAngle=softHead.Step(motion.HeadAngle*strength+lx*.8,dt,13);
            motion.HeadY=softNod.Step(motion.HeadY*strength+ly*.45,dt,14);
            motion.Hair=softHair.Step(motion.Hair*strength-motion.HeadAngle*.4,dt,7);
            motion.Limb=softLimb.Step(motion.Limb*strength,dt,10);
            motion.Angle=softAngle.Step(motion.Angle*strength,dt,12);
            motion.Breath*=strength;
            displayedMotion=motion;
            mascot.Apply(currentPose,motion);
            var frame=motion.Closed ? eyesClosed : eyesOpen;
            if(mascot.Source!=frame) mascot.Source=frame;
            mascot.BlendFrame(dt);
            double u=Math.Max(0,Math.Min(1,transition/.6));
            double eased=u*u*(3-2*u);
            speechFollow.Y=oldSpeechOffset*(1-eased)+(currentPose=="prone"?mascot.Height*.18:0)*eased;
            // Brief dissolve, followed by a slower settle, avoids half-second double bodies.
            double dissolve=Math.Max(0,Math.Min(1,transition/.14));
            dissolve=dissolve*dissolve*(3-2*dissolve);
            mascot.Opacity=dissolve;
            departingMascot.Opacity=1-dissolve;
            if(u>=1) departingMascot.Source=null;
            idleBreath.ScaleX=motion.ScaleX*(.994+.006*eased);
            idleBreath.ScaleY=motion.ScaleY*(.985+.015*eased);
            bodyRotation.Angle=motion.Angle;
            bodyMovement.X=motion.X*mascot.Width/300;
            bodyMovement.Y=motion.Y*mascot.Height/300;
            UpdateCharges(now);
        }

        private void OnAnimationFrame(object sender, EventArgs e)
        {
            if(!IsVisible || WindowState==WindowState.Minimized) return;
            double now=animationClock.Elapsed.TotalSeconds;
            int fps=sleeping||settings.QuietMode?Math.Min(30,settings.TargetFps):settings.TargetFps;
            if(now+.0005<nextFrameAt) return;
            // Advance the deadline rather than measuring from the last rendered frame.
            // This avoids alternating skipped frames on a 120 Hz display.
            nextFrameAt=now-nextFrameAt>.1?now+1.0/fps:nextFrameAt+1.0/fps;
            RenderPet(now);
        }

        private void OnScaleWheel(object sender, MouseWheelEventArgs e)
        {
            int steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            ApplyScale(settings.ScalePercent + Math.Sign(e.Delta) * 5 * steps);
            e.Handled = true;
        }

        private void UpdateLayoutMetrics()
        {
            // Resize controls and reformat text at their final size. No Viewbox or
            // bitmap scaling is allowed above the text; shrinking the mascot keeps text readable.
            double artScale = settings.ScalePercent / 100.0;
            bodyTextSize = Math.Round(settings.TextSize * Math.Max(1, Math.Min(1.35, artScale)));
            double textScale = bodyTextSize / 14.0;
            double panelWidth = Math.Round(304 * textScale);
            double panelHeight = Math.Round(280 * textScale);
            double artSize = Math.Round(300 * artScale);
            double petWidth = Math.Max(310 * textScale, artSize);
            double speechHeight = speech.Visibility == Visibility.Visible ? Math.Round(150 * textScale) : 0;
            double inputHeight = settings.HideInput?0:Math.Round(Math.Max(40, bodyTextSize * 2.7));
            petInput.Visibility=settings.HideInput?Visibility.Collapsed:Visibility.Visible;
            double leftWidth = settings.HideDataPanel ? 0 : panelWidth + 20;
            Width = scene.Width = leftWidth + petWidth + 24;
            Height = scene.Height = Math.Max(settings.HideDataPanel ? 0 : panelHeight + 24,
                speechHeight + artSize + inputHeight + 96);
            mascot.Width = mascot.Height = artSize;
            mascot.Margin = new Thickness(0, 0, (petWidth-artSize)/2 + 12, inputHeight + 22);
            departingMascot.Width = mascot.Width; departingMascot.Height = mascot.Height;
            departingMascot.HorizontalAlignment = mascot.HorizontalAlignment;
            departingMascot.VerticalAlignment = mascot.VerticalAlignment;
            departingMascot.Margin = mascot.Margin;
            UpdateHeadAnchor();
            bubble.Visibility = bubbleShadow.Visibility = settings.HideDataPanel ? Visibility.Collapsed : Visibility.Visible;
            bubble.Width = bubbleShadow.Width = panelWidth;
            bubble.Height = bubbleShadow.Height = panelHeight;
            bubble.Margin = new Thickness(12, Math.Min(speechHeight+12, Height-panelHeight-12), 0, 0);
            bubbleShadow.Margin = bubble.Margin;
            speech.Width = petWidth;
            speech.Height = Math.Max(150 * textScale, speechHeight);
            speech.Margin = new Thickness(0,12,12,0);
            tail.Visibility = speech.Visibility;
            var tailTransforms = new TransformGroup();
            tailTransforms.Children.Add(new RotateTransform(45,7.5,7.5));
            tailTransforms.Children.Add(speechFollow);
            tail.RenderTransform = tailTransforms;
            tail.Margin = new Thickness(leftWidth + petWidth*.55, speechHeight+4,0,0);
            petInput.Width = petWidth;
            petInput.Margin = new Thickness(0,0,12,12);
            speechText.FontSize = bodyTextSize;
            brand.FontSize = bodyTextSize + 1;
            titleText.FontSize = detailText.FontSize = footerText.FontSize = Math.Max(13, bodyTextSize - 1);
            titleText.Foreground = detailText.Foreground = new SolidColorBrush(Color.FromRgb(74, 94, 112));
            detailText.LineHeight = Math.Round(bodyTextSize * 1.5);
            footerText.LineHeight = Math.Round(bodyTextSize * 1.4);
            footerText.MaxHeight = footerText.LineHeight * 2;
            chatInput.FontSize = inputHint.FontSize = bodyTextSize;
            chatInputFrame.Height = sendButton.Height = inputHeight;
            foreach (Border card in chatMessages.Children)
            {
                var text = card.Child as TextBlock;
                if (text != null) { text.FontSize = bodyTextSize; text.LineHeight = Math.Round(bodyTextSize * 1.6); }
            }
        }

        private void UpdateTextSizeChecks()
        {
            foreach (MenuItem item in textSizeItems) item.IsChecked = (int)item.Tag == settings.TextSize;
            foreach (Forms.ToolStripMenuItem item in trayTextSizeItems) item.Checked = (int)item.Tag == settings.TextSize;
        }

        private void ApplyTextSize(int requested)
        {
            settings.TextSize = Math.Max(13, Math.Min(18, requested));
            UpdateTextSizeChecks();
            UpdateLayoutMetrics();
            UpdateBubble();
            SnapToWorkArea(false);
            try { SaveState(); } catch (Exception) { }
        }

        private void ApplyScale(int requested)
        {
            int percent = Math.Max(40, Math.Min(220, requested));
            if (percent == settings.ScalePercent) return;
            double centerX = Left + Width / 2.0;
            double centerY = Top + Height / 2.0;
            settings.ScalePercent = percent;
            UpdateLayoutMetrics();
            UpdateBubble();
            Left = centerX - Width / 2.0;
            Top = centerY - Height / 2.0;
            SnapToWorkArea(false);
            try { SaveState(); } catch (Exception) { }
        }
    }

    internal sealed class SettingsWindow : Window
    {
        private readonly PasswordBox password = new PasswordBox();
        private readonly TextBox visibleKey = new TextBox();
        private readonly CheckBox show = new CheckBox();
        private readonly CheckBox remember = new CheckBox();
        private readonly TextBox low = new TextBox();
        private readonly TextBox budget = new TextBox();
        private readonly TextBox interval = new TextBox();
        private readonly Slider scale = new Slider();
        private readonly TextBlock scaleLabel = new TextBlock();

        public string ApiKey { get; private set; }
        public bool RememberKey { get; private set; }
        public decimal AlertThreshold { get; private set; }
        public decimal BudgetThreshold { get; private set; }
        public int RefreshSeconds { get; private set; }
        public int ScalePercent { get; private set; }

        public SettingsWindow(string key, bool remembered, decimal threshold, decimal dailyBudget,
            int seconds, int currentScale)
        {
            Title = "小鲸鱼设置";
            Width = 478;
            Height = 585;
            MinWidth = Width;
            MinHeight = Height;
            MaxWidth = Width;
            MaxHeight = Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Brushes.White;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            StackPanel body = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
            Content = body;
            body.Children.Add(Label("DeepSeek API Key"));
            Grid keyRow = new Grid { Margin = new Thickness(0, 6, 0, 5) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition());
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            password.Password = key;
            password.Height = 32;
            password.Padding = new Thickness(8, 5, 8, 5);
            visibleKey.Text = key;
            visibleKey.Height = 32;
            visibleKey.Padding = new Thickness(8, 5, 8, 5);
            visibleKey.Visibility = Visibility.Collapsed;
            keyRow.Children.Add(password);
            keyRow.Children.Add(visibleKey);
            show.Content = "显示";
            show.VerticalAlignment = VerticalAlignment.Center;
            show.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(show, 1);
            keyRow.Children.Add(show);
            show.Checked += (s, e) =>
            {
                visibleKey.Text = password.Password;
                password.Visibility = Visibility.Collapsed;
                visibleKey.Visibility = Visibility.Visible;
            };
            show.Unchecked += (s, e) =>
            {
                password.Password = visibleKey.Text;
                visibleKey.Visibility = Visibility.Collapsed;
                password.Visibility = Visibility.Visible;
            };
            body.Children.Add(keyRow);

            remember.Content = "记住密钥（使用 Windows 当前用户加密）";
            remember.IsChecked = remembered;
            remember.Margin = new Thickness(0, 5, 0, 14);
            body.Children.Add(remember);
            body.Children.Add(Label("余额预警：低于此人民币金额时提醒（0 为关闭）"));
            ConfigureNumber(low, threshold.ToString("0.00", CultureInfo.InvariantCulture));
            body.Children.Add(low);
            body.Children.Add(Label("今日观测消耗达到此人民币金额时提醒（0 为关闭）"));
            ConfigureNumber(budget, dailyBudget.ToString("0.00", CultureInfo.InvariantCulture));
            body.Children.Add(budget);
            body.Children.Add(Label("自动刷新间隔（秒，30–300）"));
            ConfigureNumber(interval, seconds.ToString(CultureInfo.InvariantCulture));
            body.Children.Add(interval);

            scaleLabel.Text = "角色缩放（小于 100% 时保留可读字号）：" + currentScale + "%";
            scaleLabel.Margin = new Thickness(0, 10, 0, 5);
            scaleLabel.Foreground = new SolidColorBrush(Color.FromRgb(58, 80, 102));
            body.Children.Add(scaleLabel);
            scale.Minimum = 40;
            scale.Maximum = 220;
            scale.Value = currentScale;
            scale.TickFrequency = 1;
            scale.IsSnapToTickEnabled = false;
            scale.ValueChanged += (s, e) =>
                scaleLabel.Text = "角色缩放（小于 100% 时保留可读字号）：" + ((int)Math.Round(scale.Value)) + "%";
            body.Children.Add(scale);

            TextBlock note = new TextBlock {
                Text = "今日消耗由余额下降量估算，不等于官方账单。",
                Foreground = new SolidColorBrush(Color.FromRgb(104, 121, 139)),
                FontSize = 11.5,
                Margin = new Thickness(0, 14, 0, 13)
            };
            body.Children.Add(note);
            StackPanel buttons = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button cancel = new Button { Content = "取消", Width = 85, Height = 32, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            Button save = new Button { Content = "保存", Width = 85, Height = 32, IsDefault = true };
            save.Click += (s, e) => Save();
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            body.Children.Add(buttons);
        }

        private static TextBlock Label(string value)
        {
            return new TextBlock {
                Text = value,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(58, 80, 102)),
                Margin = new Thickness(0, 9, 0, 6)
            };
        }

        private static void ConfigureNumber(TextBox box, string value)
        {
            box.Text = value;
            box.Height = 30;
            box.Width = 148;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.Padding = new Thickness(8, 4, 8, 4);
        }

        private void Save()
        {
            string key = (show.IsChecked == true ? visibleKey.Text : password.Password).Trim();
            decimal a;
            decimal b;
            int seconds;
            if (key.Length == 0)
            {
                MessageBox.Show(this, "请输入 DeepSeek API Key。", "缺少密钥",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!TryMoney(low.Text, out a) || a < 0m || a > 1000000m ||
                !TryMoney(budget.Text, out b) || b < 0m || b > 1000000m)
            {
                MessageBox.Show(this, "预警和预算请输入 0 到 1000000 之间的金额。", "金额无效",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!Int32.TryParse(interval.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out seconds) ||
                seconds < 30 || seconds > 300)
            {
                MessageBox.Show(this, "刷新间隔请输入 30 到 300 秒。", "间隔无效",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ApiKey = key;
            RememberKey = remember.IsChecked == true;
            AlertThreshold = a;
            BudgetThreshold = b;
            RefreshSeconds = seconds;
            ScalePercent = (int)Math.Round(scale.Value);
            DialogResult = true;
        }

        private static bool TryMoney(string value, out decimal amount)
        {
            return Decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) ||
                Decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\DeepSeekWhaleStandalone", out first))
            {
                if (!first)
                {
                    MessageBox.Show("小鲸鱼已在运行，请查看系统托盘。", "DeepSeek 小鲸鱼",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                Forms.Application.EnableVisualStyles();
                Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.Run(new WhaleWindow());
            }
        }
    }
}
