using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SideNote;
public sealed class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Group { get; set; } = "now";
    public int Order { get; set; }
    public DateTime? Scheduled { get; set; }
    public DateTime? Completed { get; set; }
    public string? CompletedSection { get; set; }
}
public sealed class Reflection
{
    public string Text { get; set; } = "";
    // Keep earlier notes readable without changing the existing data directory.
    public string Summary { get; set; } = "";
    public string Thoughts { get; set; } = "";
}
public sealed class State
{
    public int Version { get; set; } = 1;
    public List<TaskItem> Tasks { get; set; } = new();
    public Dictionary<string, Reflection> Reflections { get; set; } = new();
    public double X { get; set; } = -1;
    public double Y { get; set; } = 160;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 610;
    public string Dock { get; set; } = "right";
}
public sealed class Store : IDisposable
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideNote");
    private readonly SqliteConnection db;
    private readonly string folder;
    public State Data { get; private set; }
    public static string ReflectionText(Reflection? entry) => entry == null ? "" :
        (!string.IsNullOrEmpty(entry.Text) ? entry.Text : string.Join("\n\n",new[]{entry.Summary,entry.Thoughts}.Where(t=>!string.IsNullOrWhiteSpace(t))));
    public IEnumerable<DateTime> RecordDates() => Data.Tasks.Where(t=>t.Completed.HasValue).Select(t=>t.Completed!.Value.Date)
        .Concat(Data.Reflections.Where(p=>!string.IsNullOrWhiteSpace(ReflectionText(p.Value))).Select(p=>DateTime.ParseExact(p.Key,"yyyy-MM-dd",CultureInfo.InvariantCulture)))
        .Distinct().OrderByDescending(d=>d);
    public static string SectionOf(TaskItem task,DateTime today) => task.Completed.HasValue && !string.IsNullOrEmpty(task.CompletedSection)
        ? task.CompletedSection! : task.Scheduled.HasValue ? (task.Scheduled.Value.Date>today.Date?"tomorrow":"now") : task.Group;
    public IEnumerable<TaskItem> VisibleTasks(string section,DateTime today) => Data.Tasks
        .Where(t=>(!t.Completed.HasValue||t.Completed.Value.Date==today.Date)&&SectionOf(t,today)==section)
        .OrderBy(t=>t.Completed.HasValue?1:0).ThenBy(t=>t.Order);
    public void ToggleCompletion(TaskItem task,DateTime now)
    {
        if(task.Completed.HasValue)
        {
            task.Completed=null;task.CompletedSection=null;
            if(task.Scheduled.HasValue&&task.Scheduled.Value.Date<=now.Date){task.Scheduled=null;task.Group="now";}
        }
        else {task.CompletedSection=SectionOf(task,now.Date);task.Completed=now;}
        task.Order=Data.Tasks.Select(t=>t.Order).DefaultIfEmpty(0).Max()+1;Save();
    }
    public void MoveTask(TaskItem item,string section,string? anchorId,bool after,DateTime today)
    {
        if(section!="now"&&section!="later"&&section!="tomorrow")throw new ArgumentException("Unknown section");
        if(item.Id==anchorId)return;
        item.Group=section=="tomorrow"?"now":section;item.Scheduled=section=="tomorrow"?today.Date.AddDays(1):(DateTime?)null;
        if(item.Completed.HasValue)item.CompletedSection=section;
        var list=VisibleTasks(section,today).Where(t=>t.Id!=item.Id&&t.Completed.HasValue==item.Completed.HasValue).ToList();
        int index=list.FindIndex(t=>t.Id==anchorId);
        if(index<0)index=list.Count;else if(after)index++;
        list.Insert(index,item);for(int i=0;i<list.Count;i++)list[i].Order=i;
        Save();
    }
    private static DataContractJsonSerializer Serializer() => new(typeof(State),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true,DateTimeFormat=new DateTimeFormat("o",CultureInfo.InvariantCulture){DateTimeStyles=DateTimeStyles.RoundtripKind},MaxItemsInObjectGraph=10000000});
    private static string Serialize(State state) {using var stream=new MemoryStream();Serializer().WriteObject(stream,state);return Encoding.UTF8.GetString(stream.ToArray());}
    public Store(string? directory = null)
    {
        folder = directory ?? DataDirectory;
        Directory.CreateDirectory(folder);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder,"notes.db") }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);"; cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT json FROM state WHERE id=1";
        var json = cmd.ExecuteScalar() as string;
        Data = json == null ? new State() : Parse(json);
        Backup();
    }
    public static State Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var state = (State?)Serializer().ReadObject(stream) ?? throw new InvalidDataException("备份为空");
        if(state.Version != 1 || state.Tasks == null || state.Reflections == null || state.Tasks.Any(t => t == null || string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Title) || (t.Group != "now" && t.Group != "later")) || state.Tasks.Select(t=>t.Id).Distinct().Count()!=state.Tasks.Count || state.Reflections.Any(p=>p.Value==null || !DateTime.TryParseExact(p.Key,"yyyy-MM-dd",null,System.Globalization.DateTimeStyles.None,out _)))
            throw new InvalidDataException("备份格式不正确或版本不支持");
        return state;
    }
    public void Save()
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO state(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json";
        cmd.Parameters.AddWithValue("$json", Serialize(Data)); cmd.ExecuteNonQuery();
        Backup();
    }
    public bool RollOver(DateTime today)
    {
        var due = Data.Tasks.Where(t=> t.Completed==null && t.Scheduled.HasValue && t.Scheduled.Value.Date<=today.Date).OrderBy(t=>t.Scheduled).ThenBy(t=>t.Order).ToList();
        int last=Data.Tasks.Where(t=>t.Completed==null && t.Group=="now" && t.Scheduled==null).Select(t=>t.Order).DefaultIfEmpty(-1).Max()+1;
        foreach(var task in due) { task.Group="now"; task.Scheduled=null; task.Order=last++; }
        if(due.Count>0) Save();
        return due.Count>0;
    }
    private void Backup()
    {
        var dir=Path.Combine(folder,"backups"); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,DateTime.Today.ToString("yyyy-MM-dd")+".json");
        if (!File.Exists(path) && (Data.Tasks.Count>0 || Data.Reflections.Count>0))
        {
            File.WriteAllText(path,Serialize(Data));
            foreach(var old in Directory.GetFiles(dir,"*.json").OrderByDescending(p=>p).Skip(14)) File.Delete(old);
        }
    }
    public void Export(string path) => File.WriteAllText(path,Serialize(Data));
    public void Import(string path)
    {
        var replacement = Parse(File.ReadAllText(path));
        Export(Path.Combine(folder,"before-import-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"));
        var previous=Data; Data=replacement;
        try { Save(); } catch { Data=previous; throw; }
    }
    public void Dispose() => db.Dispose();
    public static void Verify()
    {
        var dir=Path.Combine(Path.GetTempPath(),"SideNote-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var today=new DateTime(2026,11,20);
            using(var s=new Store(dir))
            {
                s.Data.Tasks.Add(new TaskItem {Title="keep",Order=0});
                s.Data.Tasks.Add(new TaskItem {Title="due",Scheduled=today.AddDays(-3),Group="later"});
                s.Data.Tasks.Add(new TaskItem {Title="done",Completed=today.AddDays(-1)});
                s.Data.Reflections["2026-11-19"]=new Reflection {Summary="saved",Thoughts="thought"}; s.Save();
            }
            using(var s=new Store(dir))
            {
                s.RollOver(today); s.RollOver(today);
                if(s.Data.Tasks.Count!=3 || s.Data.Tasks[1].Scheduled!=null || s.Data.Tasks[1].Group!="now" || s.Data.Tasks[1].Order<=s.Data.Tasks[0].Order || s.Data.Tasks[2].Completed!=today.AddDays(-1) || s.Data.Reflections["2026-11-19"].Summary!="saved") throw new Exception("Data invariant failed");
                if(ReflectionText(s.Data.Reflections["2026-11-19"])!="saved\n\nthought") throw new Exception("Legacy review failed");
                s.Data.Reflections["2026-11-20"]=new Reflection {Text="single daily review"};s.Save();
                if(!s.RecordDates().SequenceEqual(new[]{today,today.AddDays(-1)}))throw new Exception("Combined history failed");
                s.Export(Path.Combine(dir,"export.json")); s.Import(Path.Combine(dir,"export.json"));
            }
            using(var s=new Store(dir)) if(s.Data.Tasks.Count!=3 || ReflectionText(s.Data.Reflections["2026-11-20"])!="single daily review") throw new Exception("Restore failed");
            using(var s=new Store(Path.Combine(dir,"task-flow")))
            {
                var taskDay=new DateTime(2026,11,21);var a=new TaskItem {Title="a"};var b=new TaskItem {Title="b",Order=1};var c=new TaskItem {Title="c",Group="later"};
                s.Data.Tasks.AddRange(new[]{a,b,c});s.Save();
                s.MoveTask(a,"later",c.Id,true,taskDay);
                if(!s.VisibleTasks("later",taskDay).SequenceEqual(new[]{c,a}))throw new Exception("Cross-section drop failed");
                s.MoveTask(c,"later",a.Id,true,taskDay);
                if(!s.VisibleTasks("later",taskDay).SequenceEqual(new[]{a,c}))throw new Exception("Downward reorder failed");
                s.MoveTask(a,"tomorrow",null,false,taskDay);s.ToggleCompletion(c,taskDay.AddHours(14));
                var d=new TaskItem {Title="d",Group="later",Order=100};s.Data.Tasks.Add(d);s.Save();
                if(!s.VisibleTasks("later",taskDay).SequenceEqual(new[]{d,c}))throw new Exception("Completed task must stay last taskDay");
                s.RollOver(taskDay.AddDays(1));
                if(s.VisibleTasks("later",taskDay.AddDays(1)).Contains(c)||!s.Data.Tasks.Contains(c)||a.Scheduled!=null||a.Group!="now"||!s.VisibleTasks("later",taskDay.AddDays(1)).Contains(d))throw new Exception("Next-day retention failed");
                c.Title="edited history";s.Save();var completion=c.Completed;
                if(completion!=taskDay.AddHours(14))throw new Exception("Editing changed completion date");
                s.ToggleCompletion(c,taskDay.AddDays(1));if(c.Completed!=null)throw new Exception("Undo completion failed");
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(dir,true); }
    }
}
