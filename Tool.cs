using System.Text.Json.Nodes;
namespace WinDiagnostics;
public static class Tool
{
    public const string Name="win-service-doctor";
    public static readonly string[] ValueOptions=["--service","--days"];
    public static readonly string[] Flags=[];
    public const string Help="win-service-doctor --service Spooler [--days 7] [--json]\nReads service configuration, dependencies, executable metadata, and related Service Control Manager events.";
    public static void ValidateOptions(Options o) { if(string.IsNullOrWhiteSpace(o.Get("--service")) || o.Get("--service").Length>256 || o.Get("--service").Any(char.IsControl)) throw new ArgumentException("--service requires a service name (not a wildcard)."); o.Number("--days",7,1,30); }
    public static Task<Collection> Collect(Options o,CancellationToken ct)=>PowerShell.Collect(new(){["WD_SERVICE"]=o.Get("--service"),["WD_DAYS"]=o.Number("--days",7,1,30).ToString()},ct);
    public static void ValidateEvidence(JsonObject e){J.Require(e,"service","dependencies","events");J.RequireRows(e,"dependencies");J.RequireRows(e,"events");if(e["service"] is not null and not JsonObject)throw new ArgumentException("Evidence service must be an object or null.");}
    public static List<Finding> Analyze(JsonObject e)
    {
        var r=new List<Finding>(); if(e["service"] is not JsonObject s){r.Add(new("service-not-found","error","Service was not found","No matching service object was recorded. Missing permissions may also limit collection; inspect collection issues.","Verify the exact service name with Get-Service."));return r;}
        if(J.Text(s,"startMode")=="Disabled")r.Add(new("service-disabled","warning","Service startup is disabled","Windows will not start this service while its start mode is Disabled.","Confirm its intended startup policy before making any change."));
        if(J.Text(s,"state")!="Running")r.Add(new("service-not-running","info","Service is not running",$"Recorded state: {J.Text(s,"state")}. A stopped demand-start service may be normal.","Compare its trigger/startup policy with the symptom and recent events."));
        if(J.Number(s,"exitCode")!=0||J.Number(s,"serviceSpecificExitCode")!=0)r.Add(new("service-exit-code","warning","Service reports a nonzero exit code",$"Win32 exit: {J.Text(s,"exitCode")}; service-specific exit: {J.Text(s,"serviceSpecificExitCode")}.","Look up the Win32 code and consult this service's own logs for its specific code."));
        if(J.Text(s,"executableStatus")=="missing")r.Add(new("executable-missing","error","Configured executable could not be found",J.Text(s,"executablePath"),"Check installation integrity and the configured service image path."));
        if(J.Bool(s,"unquotedSpacedPath"))r.Add(new("unquoted-service-path","warning","Service image path contains unquoted spaces","The command line may be ambiguous. This is a configuration clue, not proof of the startup failure or an exploitable condition.","Compare the parsed executable path with the vendor's documented configuration."));
        foreach(var d in J.Rows(e,"dependencies"))if(J.Text(d,"state")!="Running")r.Add(new("dependency-not-running","warning","A prerequisite service is not running",$"{J.Text(d,"name")}: {J.Text(d,"state")}. Dependencies can start on demand.","Inspect that dependency's startup result and events before blaming it for this failure."));
        if(J.Rows(e,"events").Any())r.Add(new("service-events","info","Recent related service events are available","The report includes messages from Service Control Manager in the requested window.","Correlate their timestamps and event IDs with the failed start attempt."));return r;
    }
    public static IEnumerable<string> Describe(JsonObject e){var s=e["service"];yield return $"Service: {J.Text(s,"name","not found")} | {J.Text(s,"state")} | startup {J.Text(s,"startMode")}";if(s is not null){yield return $"Account: {J.Text(s,"account")} | PID {J.Text(s,"pid")}";yield return $"Executable: {J.Text(s,"executablePath")} ({J.Text(s,"executableStatus")})";}foreach(var d in J.Rows(e,"dependencies"))yield return $"Dependency: {J.Text(d,"name")} {J.Text(d,"state")}";foreach(var row in J.Rows(e,"events"))yield return $"{J.Text(row,"timeUtc")} | SCM {J.Text(row,"id")} | {J.Text(row,"message")}";}
    public static string[] SmokeArgs=>["--service","EventLog","--days","1"];
}
