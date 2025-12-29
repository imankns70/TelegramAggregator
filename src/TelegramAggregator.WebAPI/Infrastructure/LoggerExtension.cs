// using System.Data;
// using Serilog;
// using Serilog.Events;
// using Serilog.Filters;
// using Serilog.Sinks.MSSqlServer;

// namespace Ksc.OPC.UAMiddleware.WebApi.Extensions;

// public static class LoggerExtension
// {
//     public static void AddUaMiddlewareLogger(this IHostApplicationBuilder webApplicationBuilder)
//     {
//         {
//             try
//             {
//                 Log.Logger = new LoggerConfiguration()
//                     .WriteTo.Console()
//                     .WriteTo.File("logs.log",
//                         restrictedToMinimumLevel: LogEventLevel.Error,
//                         rollingInterval: RollingInterval.Day,
//                         fileSizeLimitBytes: 100_000_000,
//                         retainedFileCountLimit: 5,
//                         outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
//                     )
         
//                     .WriteTo.MSSqlServer(
//                         restrictedToMinimumLevel: LogEventLevel.Error,
//                         connectionString: webApplicationBuilder.Configuration.GetConnectionString("OPCClientDB"),
//                         sinkOptions: new MSSqlServerSinkOptions
//                         {
//                             SchemaName = "log",
//                             TableName = "OPC_UAMiddleware",
//                             AutoCreateSqlTable = true
//                         },
//                         columnOptions: GetGeneralColumnOptions()
//                     )
//                     // HTTP client exceptions sink
//                     .WriteTo.Logger(lc => lc
//                         .Filter.ByIncludingOnly(Matching.WithProperty<string>("Tag", tag => tag == "HttpClient"))
//                         .WriteTo.MSSqlServer(
//                             connectionString: webApplicationBuilder.Configuration.GetConnectionString("OPCClientDB"),
//                             sinkOptions: new MSSqlServerSinkOptions
//                             {
//                                 SchemaName = "Log",
//                                 TableName = "OPC_UAMiddleware_HttpClient",
//                                 AutoCreateSqlTable = true
//                             },
//                             restrictedToMinimumLevel: LogEventLevel.Error,
//                             columnOptions: GetHttpClientColumnOptions()
//                         )
//                     )
//                     .CreateLogger();
//             }
//             catch (Exception e)
//             {
//                 // Could not initialize SQL logger 
//                 Log.Logger = new LoggerConfiguration()
//                     .WriteTo.Console()
//                     .WriteTo.File("logs.log",
//                         restrictedToMinimumLevel: LogEventLevel.Error,
//                         rollingInterval: RollingInterval.Day,
//                         fileSizeLimitBytes: 100_000_000,
//                         retainedFileCountLimit: 5,
//                         outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
//                     )
//                     // HTTP client exceptions sink
//                     .WriteTo.Logger(lc => lc
//                         .Filter.ByIncludingOnly(Matching.WithProperty<string>("Tag", tag => tag == "HttpClient"))
//                     )
//                     .CreateLogger();
//             }
//         }

//         ColumnOptions GetGeneralColumnOptions()
//         {
//             var options = new ColumnOptions();
//             options.Store.Remove(StandardColumn.Properties);
//             options.Store.Add(StandardColumn.Properties);
//             return options;
//         }

//         ColumnOptions GetHttpClientColumnOptions()
//         {
//             var options = new ColumnOptions();
//             options.Store.Remove(StandardColumn.Properties);
//             options.Store.Add(StandardColumn.Properties);
//             options.AdditionalColumns =
//             [
//                 new SqlColumn { ColumnName = "HttpClient", DataType = SqlDbType.NVarChar, DataLength = 100 },
//                 new SqlColumn { ColumnName = "RequestedUrl", DataType = SqlDbType.NVarChar, DataLength = -1 },
//                 new SqlColumn { ColumnName = "InputData", DataType = SqlDbType.NVarChar, DataLength = -1 }
//             ];
//             return options;
//         }
//     }
// }