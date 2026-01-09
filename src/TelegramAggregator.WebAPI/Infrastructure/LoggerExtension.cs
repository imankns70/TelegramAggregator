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
                    schemaName:"public",
                    tableName: "Logg", 
                    needAutoCreateTable: true,
                    restrictedToMinimumLevel: LogEventLevel.Verbose,
                    columnOptions: GetGeneralColumns()
                    )
              
                .CreateBootstrapLogger();

            // این خط رو حتماً اضافه کنید (داخل try یا خارج)

        }
        catch (Exception ex)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Error()
                .WriteTo.Console()
                .CreateBootstrapLogger();

         

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