namespace ApiCallInter;

public static class AppPaths
{
    public static string DataDir =>
        Environment.GetEnvironmentVariable("APICALLINTER_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApiCallInter");

    public static string DbPath => Path.Combine(DataDir, "app.db");
    public static string LogsDir => Path.Combine(DataDir, "logs");
    public static string UpdatesDir => Path.Combine(DataDir, "updates");
}
