using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.PostgreSQL;

namespace TelegramAggregator.WebAPI.Infrastructure;

public static class LoggerExtension
{
    public static void AddSeriLogLogger(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

       

        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Verbose)
                // ---------- جدول اصلی لاگ‌ها ----------
                .WriteTo.PostgreSQL(
                    connectionString: connectionString,
                    tableName: "logs",
                    schemaName: "public",
                    needAutoCreateTable: false,
                    restrictedToMinimumLevel: LogEventLevel.Verbose,
                    columnOptions: GetGeneralColumns())
                // ---------- لاگ‌های HttpClient جداگانه ----------
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(Matching.WithProperty<string>("Tag", tag => tag == "HttpClient"))
                    .WriteTo.PostgreSQL(
                        connectionString: connectionString,
                        tableName: "log_http_client",
                        schemaName: "public",
                        needAutoCreateTable: false,
                        restrictedToMinimumLevel: LogEventLevel.Verbose,
                        columnOptions: GetHttpClientColumns()
                    )
                )
                .CreateLogger();  // ← was CreateBootstrapLogger() — bootstrap loggers are temporary
                                  //   and may never flush their sinks when no reload callback exists

            builder.Host.UseSerilog(Log.Logger, dispose: true);  // explicit, deterministic
        }
        catch (Exception ex)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Error()
                .WriteTo.Console()
                .CreateLogger();

            builder.Host.UseSerilog(Log.Logger, dispose: true);

            Log.Fatal(ex, "خطا در راه‌اندازی Serilog با PostgreSQL");
        }
    }

    private static IDictionary<string, ColumnWriterBase> GetGeneralColumns()
    {
        return new Dictionary<string, ColumnWriterBase>
        {
            ["id"] = new RenderedMessageColumnWriter(),
            ["message"] = new RenderedMessageColumnWriter(),
            ["message_template"] = new MessageTemplateColumnWriter(),
            ["level"] = new LevelColumnWriter(true),
            ["timestamp"] = new TimestampColumnWriter(),
            ["exception"] = new ExceptionColumnWriter(),
            ["properties"] = new LogEventSerializedColumnWriter()
        };
    }

    private static IDictionary<string, ColumnWriterBase> GetHttpClientColumns()
    {
        return new Dictionary<string, ColumnWriterBase>
        {
            ["id"] = new RenderedMessageColumnWriter(),
            ["message"] = new RenderedMessageColumnWriter(),
            ["message_template"] = new MessageTemplateColumnWriter(),  // حتماً اضافه کنید
            ["level"] = new LevelColumnWriter(true),
            ["timestamp"] = new TimestampColumnWriter(),
            ["exception"] = new ExceptionColumnWriter(),
            ["properties"] = new LogEventSerializedColumnWriter(),
            ["http_client"] = new SinglePropertyColumnWriter("HttpClient"),
            ["requested_url"] = new SinglePropertyColumnWriter("RequestedUrl"),
            ["input_data"] = new SinglePropertyColumnWriter("InputData")
        };
    }
}