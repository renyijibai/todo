using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SideNote;
public sealed partial class MainWindow : Window
{
    readonly Store store;
    readonly Grid root = new();
    readonly Grid shell = new();
    readonly Border frame;
    readonly TabControl tabs = new();
    readonly StackPanel tasks = new();
    readonly StackPanel history = new();
    readonly StackPanel review = new();
    readonly TextBlock status = new() { FontSize=10, Foreground=Brush("#929C89"), Margin=new Thickness(2,2,2,0),HorizontalAlignment=HorizontalAlignment.Right };
    readonly Button handle = new() {Content=new TextBlock {Text="see\nyou",TextAlignment=TextAlignment.Center,FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.Medium,FontSize=11.5,LineHeight=28,LineStackingStrategy=LineStackingStrategy.BlockLineHeight},ToolTip="see you · 点击展开，拖动调整位置",Padding=new Thickness(4),Margin=new Thickness(0),Background=Brush("#E4ECDD"),Foreground=Brush("#435A3E")};
    readonly DispatcherTimer dateTimer = new();
    readonly DispatcherTimer saveTimer = new() {Interval=TimeSpan.FromMilliseconds(500)};
    readonly Forms.NotifyIcon tray;
    readonly System.Collections.Generic.HashSet<string> collapsedDates = new();
    DateTime currentDay = DateTime.Today;
    int historyLimit=30;
    int activeTab;
    bool expanded, pinned, exiting, dialogOpen, dragging, reviewDirty, changingState, initialized, menuOpen;
    Point dragStart;
    public MainWindow(bool preview=false,string? dataDirectory=null)
    {
        store=new Store(dataDirectory);
        Title="see you";
        Width=Clamp(store.Data.Width,340,650); Height=Clamp(store.Data.Height,430,900);
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; Background=Brush("#FAF9F4");
        ShowInTaskbar=preview; FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=13;
        frame=new Border {Background=Brush("#FAF9F4"),BorderBrush=Brush("#D7DCD2"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=root};
        // Keep both views attached: replacing Window.Content on every click can retrigger
        // layout/load and focus events while the window is being resized.
        shell.Children.Add(frame);shell.Children.Add(handle);shell.Children.Add(dragOverlay);SetupTaskDragging();Content=shell;handle.Visibility=Visibility.Collapsed;
        root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        UseLayoutRounding=true;
        Resources[SystemParameters.VerticalScrollBarWidthKey]=7.0;Resources[SystemParameters.HorizontalScrollBarHeightKey]=7.0;
        var header=new Grid {Margin=new Thickness(20,18,14,14),Background=Brushes.Transparent};
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var title=new TextBlock {Text="see you",FontFamily=new FontFamily("Segoe UI"),FontSize=26,FontWeight=FontWeights.SemiBold,Foreground=Brush("#354B39"),VerticalAlignment=VerticalAlignment.Center};header.Children.Add(title);
        title.MouseLeftButtonDown+=(_,e)=> {dragging=true;try{DragMove();}finally{dragging=false;}Snap();e.Handled=true;};
        header.MouseLeftButtonDown+=(_,e)=> {if(e.OriginalSource==header){dragging=true;try{DragMove();}finally{dragging=false;}Snap();}};
        var tools=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
        var pin=IconButton("\uE718","固定展开",()=>{});pin.FontFamily=new FontFamily("Segoe MDL2 Assets");pin.FontSize=13;
        pin.Click+=(_,_)=> {pinned=!pinned;pin.Background=pinned?Brush("#DFE9D8"):Brushes.Transparent;pin.ToolTip=pinned?"取消固定":"固定展开";};
        tools.Children.Add(pin);tools.Children.Add(IconButton("⋯","更多设置",ShowMenu));tools.Children.Add(IconButton("›","收起",Collapse));Grid.SetColumn(tools,1);header.Children.Add(tools);root.Children.Add(header);
        tabs.Margin=new Thickness(18,0,18,16);tabs.Background=Brushes.Transparent;tabs.BorderThickness=new Thickness(0);
        tabs.Items.Add(Tab("待办",tasks)); tabs.Items.Add(Tab("记录",history)); tabs.Items.Add(Tab("复盘",review));
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs){FlushReview();activeTab=tabs.SelectedIndex;Render();}};
        Grid.SetRow(tabs,1); root.Children.Add(tabs);
        handle.Click+=(_,_)=>Expand();
        handle.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(this);
        handle.PreviewMouseMove+=(_,e)=> {if(!expanded&&e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(this)-dragStart).Length>8){dragging=true;try{DragMove();}finally{dragging=false;}store.Data.Y=Top;store.Data.Dock=Left<WorkArea().Left+WorkArea().Width/2?"left":"right";store.Save();Collapse();e.Handled=true;}};
        Deactivated+=(_,_)=>
        {
            if(changingState)return;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
            {if(expanded&&!IsActive&&!pinned&&!dialogOpen&&!dragging&&!changingState&&!menuOpen)Collapse();}));
        };
        Closing+=(_,e)=> {if(!exiting) {e.Cancel=true; Collapse();}};
        saveTimer.Tick+=(_,_)=>FlushReview();
        dateTimer.Tick+=(_,_)=>CheckDate();
        SystemEvents.PowerModeChanged+=OnPower;
        tray=new Forms.NotifyIcon {Text="see you · 点击展开",Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName),Visible=true};
        tray.MouseClick+=(_,e)=> {if(e.Button==Forms.MouseButtons.Left) Dispatcher.Invoke(Expand);};
        var menu=new Forms.ContextMenuStrip(); menu.Items.Add("展开便签",null,(_,_)=>Dispatcher.Invoke(Expand)); menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Exit)); tray.ContextMenuStrip=menu;
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.Escape&&!dialogOpen&&Keyboard.FocusedElement is not TextBox){Collapse();e.Handled=true;}else if(e.Key==Key.N&&Keyboard.Modifiers==ModifierKeys.Control&&!dialogOpen){Expand();EditTask(null,"now");e.Handled=true;}};
        Loaded+=(_,_)=> {if(initialized)return;initialized=true;store.RollOver(DateTime.Today);RestorePosition();Render();Collapse();if(preview)Expand();ScheduleMidnight();};
        Closed+=(_,_)=>{dateTimer.Stop();saveTimer.Stop();holdTimer.Stop();dragScrollTimer.Stop();SystemEvents.PowerModeChanged-=OnPower;tray.Dispose();store.Dispose();};
    }
    static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    static double Clamp(double value,double min,double max)=>Finite(value)?Math.Max(min,Math.Min(max,value)):min;
    static Button Button(string text,Action action) {var b=new Button {Content=text};b.Click+=(_,_)=>action();return b;}
    static Button IconButton(string symbol,string tooltip,Action action)
    {
        var button=Button(symbol,action);button.ToolTip=tooltip;button.Width=30;button.Height=30;button.Padding=new Thickness(0);button.Background=Brushes.Transparent;button.FontSize=20;
        System.Windows.Automation.AutomationProperties.SetName(button,tooltip);return button;
    }
    static TabItem Tab(string title,StackPanel panel)=>new() {Header=title,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(1,14,1,0)}};
    static TextBlock Label(string text,int size=13)=>new() {Text=text,FontSize=size,Foreground=Brush("#626E60"),Margin=new Thickness(4,12,4,8),TextWrapping=TextWrapping.Wrap};
    Rect WorkArea()
    {
        var hwnd=new WindowInteropHelper(this).Handle;
        var screen=Forms.Screen.FromHandle(hwnd).WorkingArea;
        var source=PresentationSource.FromVisual(this);
        var transform=source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(screen.Left,screen.Top)),transform.Transform(new Point(screen.Right,screen.Bottom)));
    }
    void RestorePosition()
    {
        if(Finite(store.Data.X)&&store.Data.X>=0) Left=store.Data.X;
        Top=Finite(store.Data.Y)?store.Data.Y:160;
        var area=WorkArea();
        if(store.Data.X<0) Left=area.Right-Width;
        Left=Clamp(Left,area.Left,Math.Max(area.Left,area.Right-Width)); Top=Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height));
    }
    void Snap()
    {
        var area=WorkArea();
        if(Left-area.Left<32) {store.Data.Dock="left";Left=area.Left;}
        else if(area.Right-(Left+Width)<32) {store.Data.Dock="right";Left=area.Right-Width;}
        else store.Data.Dock="float";
        Left=Clamp(Left,area.Left,Math.Max(area.Left,area.Right-Width)); Top=Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height));
        SaveGeometry();
    }
    void SaveGeometry() {if(expanded){store.Data.X=Left;store.Data.Y=Top;store.Data.Width=Width;store.Data.Height=Height;}store.Save();}
    void Expand()
    {
        if(expanded) {Activate();return;}
        changingState=true;
        try
        {
            CheckDate();expanded=true;handle.Visibility=Visibility.Collapsed;frame.Visibility=Visibility.Visible;
            Width=Clamp(store.Data.Width,340,650);Height=Clamp(store.Data.Height,430,900);RestorePosition();
            var area=WorkArea();if(store.Data.Dock=="right")Left=area.Right-Width;if(store.Data.Dock=="left")Left=area.Left;
            Background=Brush("#FAF9F4");Topmost=true;Show();Activate();
        }
        finally {Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>changingState=false));}
    }
    void Collapse()
    {
        changingState=true;
        try
        {
            FlushReview();if(expanded)SaveGeometry();expanded=false;
            frame.Visibility=Visibility.Collapsed;handle.Visibility=Visibility.Visible;
            Width=34;Height=80;Background=Brush("#E4ECDD");
            var area=WorkArea();Left=store.Data.Dock=="left"?area.Left:area.Right-Width;
            Top=Clamp(store.Data.Y,area.Top,area.Bottom-Height);Topmost=true;
        }
        finally {Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>changingState=false));}
    }
    void Render()
    {
        if(activeTab==0) {history.Children.Clear();if(!reviewDirty)review.Children.Clear();RenderTasks();}
        else if(activeTab==1) {tasks.Children.Clear();if(!reviewDirty)review.Children.Clear();RenderHistory();}
        else {tasks.Children.Clear();history.Children.Clear();RenderReview();}
        status.Text="已保存";
    }
    void Commit() {store.Save();Render();}
    bool Confirm(string message) {bool previous=dialogOpen;dialogOpen=true;try{return System.Windows.MessageBox.Show(this,message,"see you",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;}finally{dialogOpen=previous;}}
    void TrackMenu(ContextMenu menu)
    {
        menu.Opened+=(_,_)=>menuOpen=true;
        menu.Closed+=(_,_)=>{menuOpen=false;if(expanded)Activate();};
    }
    void FlushReview() {saveTimer.Stop();if(!reviewDirty)return;store.Save();reviewDirty=false;status.Text="已保存";}
    void CheckDate()
    {
        if(currentDay!=DateTime.Today) {FlushReview();currentDay=DateTime.Today;store.RollOver(currentDay);Render();}
        else if(store.RollOver(DateTime.Today))Render();
        ScheduleMidnight();
    }
    void ScheduleMidnight() {dateTimer.Stop();dateTimer.Interval=DateTime.Today.AddDays(1)-DateTime.Now+TimeSpan.FromSeconds(1);dateTimer.Start();}
    void OnPower(object sender,PowerModeChangedEventArgs e) {if(e.Mode==PowerModes.Resume)Dispatcher.BeginInvoke(new Action(CheckDate));}
    void ShowMenu()
    {
        var menu=new ContextMenu();TrackMenu(menu);
        void Add(string text,Action action) {var item=new MenuItem {Header=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("导出数据…",()=>FileOperation(false));Add("恢复备份…",()=>FileOperation(true));
        Add("打开数据目录",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.DataDirectory){UseShellExecute=true}));
        Add("调整窗口大小…",ResizeDialog);
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {bool enabled=key?.GetValue("SideNote")!=null;Add(enabled?"关闭开机启动":"开启开机启动",()=>{using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(enabled)run.DeleteValue("SideNote",false);else run.SetValue("SideNote","\""+System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName+"\"");});}
        Add("退出",Exit);menu.PlacementTarget=this;menu.IsOpen=true;
    }
    void ResizeDialog()
    {
        dialogOpen=true;try {var dialog=new Window {Owner=this,Title="窗口大小",Background=Brush("#FAF9F4"),Width=300,Height=230,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var panel=new StackPanel {Margin=new Thickness(16)};dialog.Content=panel;
            panel.Children.Add(Label("宽度 340–650"));var w=new Slider {Minimum=340,Maximum=650,Value=Width};panel.Children.Add(w);panel.Children.Add(Label("高度 430–900"));var h=new Slider {Minimum=430,Maximum=900,Value=Height};panel.Children.Add(h);
            panel.Children.Add(Button("应用",()=>{Width=w.Value;Height=h.Value;Snap();dialog.Close();}));dialog.ShowDialog();}finally{dialogOpen=false;}
    }
    void FileOperation(bool import)
    {
        FlushReview();dialogOpen=true;
        try
        {
            if(import){var open=new OpenFileDialog {Filter="see you备份 (*.json)|*.json"};if(open.ShowDialog(this)==true&&Confirm("恢复会替换当前数据，并在本地保留恢复前的副本。继续？")){store.Import(open.FileName);store.RollOver(DateTime.Today);Render();}}
            else {var save=new SaveFileDialog {Filter="see you备份 (*.json)|*.json",FileName="see you备份-"+DateTime.Today.ToString("yyyyMMdd")+".json"};if(save.ShowDialog(this)==true){store.Export(save.FileName);status.Text="备份已导出";}}
        }finally{dialogOpen=false;}
    }
    void Exit() {FlushReview();if(expanded)SaveGeometry();exiting=true;Close();System.Windows.Application.Current.Shutdown();}
    internal void CloseForCheck(){exiting=true;Close();}
    // A focused regression for the disappearing handle and the new in-place editor.
    // Called only with --verify-window and an isolated data directory.
    internal async System.Threading.Tasks.Task VerifyWindowAsync(string outputDirectory)
    {
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        for(int i=0;i<2;i++)
        {
            handle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if(!expanded||!frame.IsVisible||handle.IsVisible||Content!=shell||ActualWidth<340)throw new InvalidOperationException("展开后界面不可见");
            Collapse();await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if(expanded||!handle.IsVisible||frame.IsVisible||ActualWidth!=34||ActualHeight!=80)throw new InvalidOperationException("收起后标签不可见");
        }
        Snapshot(System.IO.Path.Combine(outputDirectory,"handle.png"));
        Expand();
        if(dropZones.Single(z=>z.Section=="now").Body.Visibility!=Visibility.Visible||dropZones.Where(z=>z.Section!="now").Any(z=>z.Body.Visibility!=Visibility.Collapsed))throw new InvalidOperationException("分组默认展开状态错误");
        var nowZone=dropZones.Single(z=>z.Section=="now");nowZone.Toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(nowZone.Body.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("点击分组未收起");
        nowZone.Toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(nowZone.Body.Visibility!=Visibility.Visible)throw new InvalidOperationException("点击分组未展开");
        Snapshot(System.IO.Path.Combine(outputDirectory,"sections.png"));
        EditTask(null,"later");
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var draft=drafts.Single();
        if(OwnedWindows.Count!=0||draft.Input==null||!draft.Input.IsVisible)throw new InvalidOperationException("未在原位显示输入区");
        if(collapsedSections.Contains("later"))throw new InvalidOperationException("新增任务未自动展开分组");
        draft.Input.Text="整理这周的想法";draft.Details="把零散的想法记下来，慢慢完成。";
        RenderTasks();UpdateLayout();Snapshot(System.IO.Path.Combine(outputDirectory,"inline.png"));SaveDraft(draft);
        if(!store.Data.Tasks.Any(t=>t.Title=="整理这周的想法"&&t.Group=="later"))throw new InvalidOperationException("原位保存失败");
        var a=new TaskItem {Title="完成一件重要的小事",Details="先处理最重要的一步。",Group="now",Order=0};
        var b=new TaskItem {Title="整理桌面",Details="文件与笔记各归其位。",Group="now",Order=1};
        var done=new TaskItem {Title="读完今天的文章",Details="留下两条值得记住的想法。",Group="now",Order=2};
        store.Data.Tasks.AddRange(new[]{a,b,done});store.ToggleCompletion(done,DateTime.Now);RenderTasks();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var later=dropZones.Single(z=>z.Section=="later");var row=later.Rows.First().Item2;var point=row.TranslatePoint(new Point(15,row.ActualHeight-1),shell);
        dragItem=a;UpdateTaskDrag(point);
        if(dropSection!="later"||!dropAfter)throw new InvalidOperationException("跨组落点识别失败");
        store.MoveTask(a,dropSection!,dropAnchor,dropAfter,DateTime.Today);CancelTaskDrag();RenderTasks();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        later=dropZones.Single(z=>z.Section=="later");row=later.Rows.First().Item2;point=row.TranslatePoint(new Point(15,1),shell);dragItem=a;UpdateTaskDrag(point);
        if(dropSection!="later"||dropAfter)throw new InvalidOperationException("向前排序落点识别失败");
        store.MoveTask(a,dropSection!,dropAnchor,dropAfter,DateTime.Today);CancelTaskDrag();RenderTasks();
        if(store.VisibleTasks("later",DateTime.Today).First()!=a)throw new InvalidOperationException("向前排序失败");
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Snapshot(System.IO.Path.Combine(outputDirectory,"todo.png"));
        store.Data.Reflections[currentDay.ToString("yyyy-MM-dd")]=new Reflection {Text="今天专注完成了一件重要的小事。\n\n给自己留一点余地，明天继续。"};
        store.Save();tabs.SelectedIndex=2;
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Snapshot(System.IO.Path.Combine(outputDirectory,"review.png"));
        var reviewEditor=(TextBox)review.Children[0];
        if(reviewEditor.CaretIndex!=reviewEditor.Text.Length)throw new InvalidOperationException("复盘默认光标不在末尾");
        tabs.SelectedIndex=1;await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        EditTask(done,done.Group,null,true);await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var historyDraft=drafts.Single();historyDraft.Input!.Text="修改后的完成记录";var completedAt=done.Completed;SaveDraft(historyDraft);
        if(done.Title!="修改后的完成记录"||done.Completed!=completedAt)throw new InvalidOperationException("历史任务编辑失败");
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var editReview=VisualChildren<Button>(history).Last(button=>(string?)button.ToolTip=="编辑");editReview.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        VisualChildren<TextBox>(history).Single().Text="修改后的复盘记录";
        VisualChildren<Button>(history).Single(button=>(string?)button.ToolTip=="保存").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(Store.ReflectionText(store.Data.Reflections[currentDay.ToString("yyyy-MM-dd")])!="修改后的复盘记录")throw new InvalidOperationException("历史复盘编辑失败");
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Snapshot(System.IO.Path.Combine(outputDirectory,"history.png"));
        VisualChildren<Button>(history).Last(button=>(string?)button.ToolTip=="删除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        VisualChildren<Button>(history).Single(button=>(string?)button.ToolTip=="确认删除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(store.Data.Reflections.ContainsKey(currentDay.ToString("yyyy-MM-dd")))throw new InvalidOperationException("历史复盘删除失败");
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        VisualChildren<Button>(history).Single(button=>(string?)button.ToolTip=="删除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        VisualChildren<Button>(history).Single(button=>(string?)button.ToolTip=="确认删除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if(store.Data.Tasks.Contains(done))throw new InvalidOperationException("历史任务删除失败");
    }
    static System.Collections.Generic.IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)yield return value;
            foreach(var nested in VisualChildren<T>(child))yield return nested;
        }
    }
    void Snapshot(string path)
    {
        UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(shell);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file=System.IO.File.Create(path);encoder.Save(file);
    }
}
