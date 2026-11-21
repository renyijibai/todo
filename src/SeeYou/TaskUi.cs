using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SideNote;
public sealed partial class MainWindow
{
    readonly List<TaskDraft> drafts=new();
    readonly List<DropZone> dropZones=new();
    readonly HashSet<string> collapsedSections=new() {"later","tomorrow"};
    readonly Canvas dragOverlay=new() {IsHitTestVisible=false};
    readonly Border dropLine=new() {Height=2,Background=Brush("#79966A"),CornerRadius=new CornerRadius(1),Visibility=Visibility.Collapsed};
    readonly Border dragGhost=new() {Background=Brush("#E2EAD8"),BorderBrush=Brush("#BBCBAE"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(10,7,10,7),MaxWidth=240,Visibility=Visibility.Collapsed};
    readonly DispatcherTimer holdTimer=new() {Interval=TimeSpan.FromMilliseconds(260)};
    readonly DispatcherTimer dragScrollTimer=new() {Interval=TimeSpan.FromMilliseconds(75)};
    TaskItem? dragItem;
    Border? dragCard;
    Point taskPress;
    string? dropSection,dropAnchor,deletingTaskId;
    bool dropAfter;
    sealed class TaskDraft
    {
        public TaskItem? Existing;
        public string Group="now",Title="",Details="";
        public DateTime? Scheduled;
        public TextBox? Input;
        public bool History;
    }
    sealed class DropZone
    {
        public string Section="now";
        public StackPanel Panel=new();
        public Grid Header=new();
        public StackPanel Body=new();
        public Button Toggle=new();
        public Path Chevron=new();
        public List<Tuple<TaskItem,FrameworkElement>> Rows=new();
    }
    static Button GlyphButton(string kind,string name,Action action)
    {
        string geometry=kind switch {
            "edit"=>"M2,12 L3,8 L11,0 L14,3 L6,11 Z M9,2 L12,5",
            "delete"=>"M2,4 L12,4 M5,4 L5,2 L9,2 L9,4 M3,4 L4,13 L10,13 L11,4 M6,6 L6,11 M8,6 L8,11",
            "undo"=>"M5,2 L1,6 L5,10 M1,6 L9,6 C14,6 14,13 9,13",
            "save"=>"M2,7 L6,11 L13,3",
            _=>"M3,3 L11,11 M11,3 L3,11"};
        var path=new Path {Data=Geometry.Parse(geometry),StrokeThickness=1.35,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=13,Height=13};
        var button=IconButton("",name,action);button.Content=path;button.Width=25;button.Height=25;button.Margin=new Thickness(0);button.Foreground=Brush("#839477");
        path.SetBinding(Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") {Source=button});return button;
    }
    void RenderTasks()
    {
        CancelTaskDrag();tasks.Children.Clear();dropZones.Clear();
        Section("立即做","now",null);Section("以后做","later",null);Section("明天做","tomorrow",DateTime.Today.AddDays(1));
    }
    void Section(string title,string section,DateTime? schedule)
    {
        var items=store.VisibleTasks(section,currentDay).ToList();
        bool opened=!collapsedSections.Contains(section);
        var panel=new StackPanel {Background=Brushes.Transparent,Margin=new Thickness(0,0,0,18)};
        var heading=new Grid {Margin=new Thickness(3,0,0,7),Background=Brushes.Transparent};
        heading.ColumnDefinitions.Add(new ColumnDefinition());heading.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var names=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var chevron=new Path {Data=Geometry.Parse("M0,0 L4,4 L8,0"),Stroke=Brush(section=="now"?"#7E966E":section=="later"?"#B8AB91":"#92A5AE"),StrokeThickness=1.4,Width=8,Height=4,Margin=new Thickness(1,0,9,0),RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(opened?0:-90)};
        names.Children.Add(chevron);
        names.Children.Add(new TextBlock {Text=title,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=Brush("#485841"),VerticalAlignment=VerticalAlignment.Center});
        names.Children.Add(new TextBlock {Text=items.Count(t=>!t.Completed.HasValue).ToString(),FontSize=11,Foreground=Brush("#9BA391"),Margin=new Thickness(9,1,0,0),VerticalAlignment=VerticalAlignment.Center});
        var toggle=Button("",()=>SetSectionExpanded(section,collapsedSections.Contains(section)));toggle.Content=names;toggle.Background=Brushes.Transparent;toggle.Padding=new Thickness(3,5,4,5);toggle.Margin=new Thickness(-3,0,4,0);toggle.HorizontalContentAlignment=HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(toggle,(opened?"收起":"展开")+title);heading.Children.Add(toggle);
        var add=IconButton("＋","添加",()=>EditTask(null,section=="tomorrow"?"now":section,schedule));add.Background=Brush("#EEF1E8");add.Width=27;add.Height=27;add.FontSize=18;Grid.SetColumn(add,1);heading.Children.Add(add);panel.Children.Add(heading);
        var body=new StackPanel {Background=Brushes.Transparent,MinHeight=25,Visibility=opened?Visibility.Visible:Visibility.Collapsed};panel.Children.Add(body);
        var zone=new DropZone {Section=section,Panel=panel,Header=heading,Body=body,Toggle=toggle,Chevron=chevron};dropZones.Add(zone);
        var pending=drafts.Where(d=>!d.History&&d.Existing==null&&DraftGroup(d)==section).ToList();
        foreach(var draft in pending)body.Children.Add(InlineEditor(draft));
        foreach(var task in items)
        {
            var draft=drafts.FirstOrDefault(d=>!d.History&&d.Existing==task);
            var row=draft==null?TaskCard(task,false):InlineEditor(draft);body.Children.Add(row);zone.Rows.Add(Tuple.Create(task,(FrameworkElement)row));
        }
        if(items.Count==0&&pending.Count==0)body.Children.Add(new Border {Height=25,Background=Brushes.Transparent,BorderBrush=Brush("#E7EADF"),BorderThickness=new Thickness(0,0,0,1)});
        tasks.Children.Add(panel);
    }
    void SetSectionExpanded(string section,bool opened)
    {
        if(opened)collapsedSections.Remove(section);else collapsedSections.Add(section);
        var zone=dropZones.FirstOrDefault(z=>z.Section==section);if(zone==null)return;
        zone.Body.Visibility=opened?Visibility.Visible:Visibility.Collapsed;
        zone.Chevron.RenderTransform=new RotateTransform(opened?0:-90);
        System.Windows.Automation.AutomationProperties.SetName(zone.Toggle,(opened?"收起":"展开")+(section=="now"?"立即做":section=="later"?"以后做":"明天做"));
    }
    Border TaskCard(TaskItem task,bool inHistory)
    {
        bool done=task.Completed.HasValue;
        var card=new Border {Background=Brush(done&&!inHistory?"#F0F1E9":"#F2F3EC"),CornerRadius=new CornerRadius(8),BorderBrush=Brush("#E5E9DD"),BorderThickness=new Thickness(0,0,0,1),Margin=new Thickness(1,2,1,2),Padding=new Thickness(10,8,8,8)};
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(27)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});card.Child=grid;
        if(!inHistory)
        {
            var check=new CheckBox {IsChecked=done,Width=17,Height=17,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3.5,0,0)};
            System.Windows.Automation.AutomationProperties.SetName(check,done?"取消完成":"完成");
            check.Click+=(_,_)=>{store.ToggleCompletion(task,DateTime.Now);Render();};grid.Children.Add(check);
        }
        else
        {
            var tick=new Path {Data=Geometry.Parse("M1,7 L5,11 L13,2"),Stroke=Brush("#879C77"),StrokeThickness=1.5,Width=15,Height=17,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3.5,0,0)};grid.Children.Add(tick);
        }
        var text=new TextBlock {Text=task.Title,TextWrapping=TextWrapping.Wrap,FontSize=13,FontWeight=FontWeights.Normal,LineHeight=24,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=Brush(done&&!inHistory?"#969F8D":"#3F5039")};
        if(done&&!inHistory)text.TextDecorations=TextDecorations.Strikethrough;Grid.SetColumn(text,1);grid.Children.Add(text);
        if(!string.IsNullOrWhiteSpace(task.Details))
        {
            var detail=new TextBlock {Text=task.Details,TextWrapping=TextWrapping.Wrap,FontSize=12,FontWeight=FontWeights.Normal,LineHeight=21,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=Brush(done&&!inHistory?"#A3AA9A":"#859077"),Margin=new Thickness(0,3,0,1)};
            Grid.SetColumn(detail,1);Grid.SetColumnSpan(detail,2);Grid.SetRow(detail,1);grid.Children.Add(detail);
        }
        var actions=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(actions,2);grid.Children.Add(actions);
        if(deletingTaskId==task.Id)
        {
            actions.Children.Add(GlyphButton("cancel","取消",()=>{deletingTaskId=null;Render();}));
            actions.Children.Add(GlyphButton("save","确认删除",()=>{store.Data.Tasks.Remove(task);drafts.RemoveAll(d=>d.Existing==task);deletingTaskId=null;store.Save();Render();}));
        }
        else
        {
            if(inHistory)actions.Children.Add(GlyphButton("undo","恢复待办",()=>{store.ToggleCompletion(task,DateTime.Now);Render();}));
            actions.Children.Add(GlyphButton("edit","编辑",()=>EditTask(task,task.Group,null,inHistory)));
            actions.Children.Add(GlyphButton("delete","删除",()=>{deletingTaskId=task.Id;Render();}));
            actions.Opacity=.4;card.MouseEnter+=(_,_)=>actions.Opacity=1;card.MouseLeave+=(_,_)=>{if(!actions.IsKeyboardFocusWithin)actions.Opacity=.4;};actions.IsKeyboardFocusWithinChanged+=(_,_)=>actions.Opacity=actions.IsKeyboardFocusWithin||card.IsMouseOver?1:.4;
        }
        if(!inHistory)WireTaskDrag(card,task);
        return card;
    }
    void EditTask(TaskItem? existing,string group,DateTime? schedule=null,bool fromHistory=false)
    {
        FlushReview();var draft=existing==null?drafts.FirstOrDefault(d=>d.Existing==null&&d.Group==group&&d.Scheduled==schedule):drafts.FirstOrDefault(d=>d.Existing==existing);
        if(draft==null){draft=new TaskDraft {Existing=existing,Group=group,Scheduled=existing?.Scheduled??schedule,Title=existing?.Title??"",Details=existing?.Details??""};drafts.Add(draft);}
        draft.History=fromHistory;int target=fromHistory?1:0;
        if(!fromHistory)collapsedSections.Remove(DraftGroup(draft));
        if(tabs.SelectedIndex!=target)tabs.SelectedIndex=target;else Render();
        Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{draft.Input?.BringIntoView();draft.Input?.Focus();}));
    }
    string DraftGroup(TaskDraft draft)=>draft.Scheduled.HasValue?(draft.Scheduled.Value.Date>DateTime.Today?"tomorrow":"now"):draft.Group;
    Border InlineEditor(TaskDraft draft)
    {
        var panel=new StackPanel();var card=new Border {Background=Brush("#EFF2E7"),BorderBrush=Brush("#DDE4D4"),BorderThickness=new Thickness(0,0,0,1),CornerRadius=new CornerRadius(8),Padding=new Thickness(13),Margin=new Thickness(1,3,1,3),Child=panel};
        var title=new TextBox {Text=draft.Title,Style=(Style)FindResource("QuietEditor"),MaxLength=300,FontSize=13,FontWeight=FontWeights.Normal,Padding=new Thickness(0,2,0,8)};
        var detail=new TextBox {Text=draft.Details,Style=(Style)FindResource("QuietEditor"),MaxLength=20000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,MaxHeight=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=12,FontWeight=FontWeights.Normal,Foreground=Brush("#7F8B73"),Padding=new Thickness(0,9,0,5)};
        System.Windows.Automation.AutomationProperties.SetName(title,"要做的事");System.Windows.Automation.AutomationProperties.SetName(detail,"补充内容");draft.Input=title;
        title.TextChanged+=(_,_)=>{draft.Title=title.Text;card.BorderBrush=Brush("#DDE4D4");};detail.TextChanged+=(_,_)=>draft.Details=detail.Text;
        panel.Children.Add(title);panel.Children.Add(new Border {Height=1,Background=Brush("#DEE5D5")});panel.Children.Add(detail);
        void Save(){if(string.IsNullOrWhiteSpace(draft.Title)){card.BorderBrush=Brush("#BA987B");title.Focus();return;}SaveDraft(draft);}
        void Cancel(){drafts.Remove(draft);Render();}
        title.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Save();e.Handled=true;}else if(e.Key==Key.Escape){Cancel();e.Handled=true;}};
        detail.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){Save();e.Handled=true;}else if(e.Key==Key.Escape){Cancel();e.Handled=true;}};
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,4,0,0)};actions.Children.Add(GlyphButton("cancel","取消",Cancel));var save=GlyphButton("save","保存",Save);save.Background=Brush("#DFE9D5");actions.Children.Add(save);panel.Children.Add(actions);return card;
    }
    void SaveDraft(TaskDraft draft)
    {
        var task=draft.Existing??new TaskItem {Group=draft.Group,Scheduled=draft.Scheduled,Order=store.Data.Tasks.Select(t=>t.Order).DefaultIfEmpty(0).Max()+1};task.Title=draft.Title.Trim();task.Details=draft.Details.Trim();
        if(!task.Completed.HasValue&&task.Scheduled.HasValue&&task.Scheduled.Value.Date<=DateTime.Today){task.Group="now";task.Scheduled=null;}
        if(draft.Existing==null)store.Data.Tasks.Add(task);store.Save();drafts.Remove(draft);Render();
    }
    void SetupTaskDragging()
    {
        dragOverlay.Children.Add(dropLine);dragOverlay.Children.Add(dragGhost);
        holdTimer.Tick+=(_,_)=>{holdTimer.Stop();if(dragCard!=null&&Mouse.LeftButton==MouseButtonState.Pressed&&dragCard.IsMouseCaptured)BeginTaskDrag();};
        dragScrollTimer.Tick+=(_,_)=>{if(!dragging||dragItem==null)return;var scroll=(ScrollViewer)((TabItem)tabs.Items[0]).Content;var p=Mouse.GetPosition(scroll);if(p.Y<28)scroll.LineUp();else if(p.Y>scroll.ActualHeight-28)scroll.LineDown();UpdateTaskDrag(Mouse.GetPosition(shell));};
    }
    static bool IsAction(DependencyObject? source,DependencyObject row)
    {
        while(source!=null&&source!=row)
        {
            if(source is ButtonBase||source is TextBox)return true;
            source=source is FrameworkContentElement content?content.Parent:source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    void WireTaskDrag(Border card,TaskItem task)
    {
        card.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(IsAction(e.OriginalSource as DependencyObject,card))return;
            if(e.ClickCount==2){CancelTaskDrag();EditTask(task,task.Group);e.Handled=true;return;}
            CancelTaskDrag();dragCard=card;dragItem=task;taskPress=e.GetPosition(shell);card.CaptureMouse();holdTimer.Start();e.Handled=true;
        };
        card.PreviewMouseMove+=(_,e)=>
        {
            if(dragCard!=card||dragItem==null||e.LeftButton!=MouseButtonState.Pressed)return;
            var p=e.GetPosition(shell);if(!dragging&&(Math.Abs(p.X-taskPress.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(p.Y-taskPress.Y)>SystemParameters.MinimumVerticalDragDistance))BeginTaskDrag();
            if(dragging)UpdateTaskDrag(p);e.Handled=true;
        };
        card.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(dragCard!=card)return;
            var item=dragItem;string? section=dropSection,anchor=dropAnchor;bool after=dropAfter,commit=dragging&&section!=null;
            CancelTaskDrag();if(commit&&item!=null){store.MoveTask(item,section!,anchor,after,DateTime.Today);Render();}e.Handled=true;
        };
        card.LostMouseCapture+=(_,_)=>{if(dragCard==card)CancelTaskDrag();};
    }
    void BeginTaskDrag()
    {
        holdTimer.Stop();if(dragItem==null||dragCard==null)return;dragging=true;dragCard.Opacity=.45;Mouse.OverrideCursor=Cursors.SizeAll;
        dragGhost.Child=new TextBlock {Text=dragItem.Title,FontSize=12,FontWeight=FontWeights.Normal,Foreground=Brush("#4E6442"),TextTrimming=TextTrimming.CharacterEllipsis};dragGhost.Visibility=Visibility.Visible;dragScrollTimer.Start();UpdateTaskDrag(Mouse.GetPosition(shell));
    }
    void UpdateTaskDrag(Point point)
    {
        if(dragItem==null)return;
        Canvas.SetLeft(dragGhost,Clamp(point.X+12,4,Math.Max(4,ActualWidth-245)));Canvas.SetTop(dragGhost,Clamp(point.Y+16,4,Math.Max(4,ActualHeight-40)));
        var scroll=(ScrollViewer)((TabItem)tabs.Items[0]).Content;var origin=scroll.TranslatePoint(new Point(),shell);
        if(point.X<origin.X||point.X>origin.X+scroll.ActualWidth||point.Y<origin.Y||point.Y>origin.Y+scroll.ActualHeight){dropSection=null;dropLine.Visibility=Visibility.Collapsed;return;}
        var zone=dropZones.OrderBy(z=>
        {
            var top=z.Panel.TranslatePoint(new Point(),shell).Y;var bottom=top+z.Panel.ActualHeight;return point.Y<top?top-point.Y:point.Y>bottom?point.Y-bottom:0;
        }).FirstOrDefault();
        if(zone==null)return;dropSection=zone.Section;dropAnchor=null;dropAfter=false;
        var rows=zone.Rows.Where(r=>!collapsedSections.Contains(zone.Section)&&r.Item1!=dragItem&&r.Item1.Completed.HasValue==dragItem.Completed.HasValue).ToList();
        FrameworkElement target=zone.Header;bool afterTarget=true;
        foreach(var row in rows)
        {
            var top=row.Item2.TranslatePoint(new Point(),shell).Y;target=row.Item2;dropAnchor=row.Item1.Id;
            if(point.Y<top+row.Item2.ActualHeight/2){dropAfter=false;afterTarget=false;break;}
            dropAfter=true;afterTarget=true;
        }
        if(rows.Count==0&&dragItem.Completed.HasValue&&!collapsedSections.Contains(zone.Section))
        {
            var pending=zone.Rows.LastOrDefault(r=>!r.Item1.Completed.HasValue);if(pending!=null)target=pending.Item2;
        }
        var location=target.TranslatePoint(new Point(),shell);double y=location.Y+(afterTarget?target.ActualHeight+2:-2);
        Canvas.SetLeft(dropLine,zone.Panel.TranslatePoint(new Point(),shell).X+1);Canvas.SetTop(dropLine,y);dropLine.Width=Math.Max(10,zone.Panel.ActualWidth-2);dropLine.Visibility=Visibility.Visible;
    }
    void CancelTaskDrag()
    {
        holdTimer.Stop();dragScrollTimer.Stop();var card=dragCard;dragCard=null;dragItem=null;dropSection=null;
        if(card!=null){card.Opacity=1;if(card.IsMouseCaptured)card.ReleaseMouseCapture();}
        dragging=false;Mouse.OverrideCursor=null;dropLine.Visibility=Visibility.Collapsed;dragGhost.Visibility=Visibility.Collapsed;
    }
}
