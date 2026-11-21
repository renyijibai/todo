using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SideNote;
public sealed partial class MainWindow
{
    readonly Dictionary<string,string> reflectionDrafts=new();
    string? deletingReflectionKey;
    void RenderHistory()
    {
        history.Children.Clear();var dates=store.RecordDates().ToList();
        foreach(var date in dates.Take(historyLimit))
        {
            var completed=store.Data.Tasks.Where(t=>t.Completed?.Date==date).OrderBy(t=>t.Order).ToList();string key=date.ToString("yyyy-MM-dd");
            var heading=new StackPanel {Orientation=Orientation.Horizontal};
            heading.Children.Add(new TextBlock {Text=date.ToString("yyyy年M月d日"),FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=Brush("#465841")});
            if(completed.Count>0)heading.Children.Add(new TextBlock {Text="  ·  "+completed.Count,FontSize=11,Foreground=Brush("#98A18F"),VerticalAlignment=VerticalAlignment.Center});
            var exp=new Expander {Header=heading,IsExpanded=!collapsedDates.Contains(key),Margin=new Thickness(0,0,0,14),Padding=new Thickness(8),Background=Brush("#F2F3EC"),BorderThickness=new Thickness(0)};
            exp.Expanded+=(_,_)=>collapsedDates.Remove(key);exp.Collapsed+=(_,_)=>collapsedDates.Add(key);var list=new StackPanel();exp.Content=list;
            foreach(var task in completed)
            {
                var draft=drafts.FirstOrDefault(d=>d.History&&d.Existing==task);list.Children.Add(draft==null?TaskCard(task,true):InlineEditor(draft));
            }
            store.Data.Reflections.TryGetValue(key,out var entry);var reflection=Store.ReflectionText(entry);
            if(!string.IsNullOrWhiteSpace(reflection)||reflectionDrafts.ContainsKey(key))
            {
                var note=new Border {Background=Brushes.Transparent,Padding=new Thickness(10,10,4,5),Margin=new Thickness(0,6,0,0)};
                var panel=new StackPanel();note.Child=panel;
                if(completed.Count>0)panel.Children.Add(new Border {Height=1,Background=Brush("#E1E6D8"),Margin=new Thickness(0,0,6,10)});
                if(reflectionDrafts.TryGetValue(key,out var draftText))
                {
                    var editor=new TextBox {Text=draftText,Style=(Style)FindResource("QuietEditor"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=90,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=13,FontWeight=FontWeights.Normal,Padding=new Thickness(0,3,5,8),MaxLength=100000};TextBlock.SetLineHeight(editor,25);
                    System.Windows.Automation.AutomationProperties.SetName(editor,"编辑复盘记录");editor.TextChanged+=(_,_)=>reflectionDrafts[key]=editor.Text;panel.Children.Add(editor);
                    var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
                    void Cancel(){reflectionDrafts.Remove(key);RenderHistory();}
                    void Save(){store.Data.Reflections[key]=new Reflection {Text=reflectionDrafts[key]};store.Save();reflectionDrafts.Remove(key);RenderHistory();}
                    actions.Children.Add(GlyphButton("cancel","取消",Cancel));actions.Children.Add(GlyphButton("save","保存",Save));panel.Children.Add(actions);
                    editor.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Cancel();e.Handled=true;}else if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){Save();e.Handled=true;}};
                    editor.Loaded+=(_,_)=>editor.Focus();
                }
                else
                {
                    var text=new TextBlock {Text=reflection,TextWrapping=TextWrapping.Wrap,FontSize=13,FontWeight=FontWeights.Normal,Foreground=Brush("#627456"),LineHeight=25};panel.Children.Add(text);
                    var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,5,0,0)};
                    if(deletingReflectionKey==key)
                    {
                        actions.Children.Add(GlyphButton("cancel","取消",()=>{deletingReflectionKey=null;RenderHistory();}));
                        actions.Children.Add(GlyphButton("save","确认删除",()=>{store.Data.Reflections.Remove(key);reflectionDrafts.Remove(key);deletingReflectionKey=null;store.Save();RenderHistory();}));
                    }
                    else
                    {
                        actions.Children.Add(GlyphButton("edit","编辑",()=>{reflectionDrafts[key]=reflection;RenderHistory();}));
                        actions.Children.Add(GlyphButton("delete","删除",()=>{deletingReflectionKey=key;RenderHistory();}));
                        actions.Opacity=.4;note.MouseEnter+=(_,_)=>actions.Opacity=1;note.MouseLeave+=(_,_)=>{if(!actions.IsKeyboardFocusWithin)actions.Opacity=.4;};actions.IsKeyboardFocusWithinChanged+=(_,_)=>actions.Opacity=actions.IsKeyboardFocusWithin||note.IsMouseOver?1:.4;
                    }
                    panel.Children.Add(actions);
                }
                list.Children.Add(note);
            }
            history.Children.Add(exp);
        }
        if(dates.Count>historyLimit)history.Children.Add(Button("…",()=>{historyLimit+=30;RenderHistory();}));
    }
    void RenderReview()
    {
        if(reviewDirty)return;review.Children.Clear();
        var key=currentDay.ToString("yyyy-MM-dd");store.Data.Reflections.TryGetValue(key,out var entry);
        var editor=new TextBox {Text=Store.ReflectionText(entry),Style=(Style)FindResource("QuietEditor"),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=Math.Max(220,Height-165),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=100000,Padding=new Thickness(3,2,5,10),FontSize=14,FontWeight=FontWeights.Normal};TextBlock.SetLineHeight(editor,27);
        System.Windows.Automation.AutomationProperties.SetName(editor,"当天复盘");
        editor.CaretIndex=editor.Text.Length;
        editor.Loaded+=(_,_)=>{if(activeTab==2&&editor.IsVisible){editor.Focus();editor.CaretIndex=editor.Text.Length;editor.ScrollToEnd();}};
        editor.TextChanged+=(_,_)=>{store.Data.Reflections[key]=new Reflection {Text=editor.Text};reviewDirty=true;saveTimer.Stop();saveTimer.Start();};
        editor.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}};
        review.Children.Add(editor);
    }
}
