using DriftAnalytics.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.Json;

namespace DriftAnalytics.Services;

public class WebServer : BackgroundService
{
    private readonly MetricsCollector _metricsCollector;
    private readonly ILogger<WebServer> _logger;
    private readonly HttpListener _listener;
    private readonly string _url = "http://localhost:8080/";
    
    public WebServer(MetricsCollector metricsCollector, ILogger<WebServer> logger)
    {
        _metricsCollector = metricsCollector;
        _logger = logger;
        _listener = new HttpListener();
        _listener.Prefixes.Add(_url);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("Web server started on {Url}", _url);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in web server");
        }
        finally
        {
            _listener.Stop();
        }
    }
    
    private async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            
            _logger.LogDebug("Request: {Method} {Url}", request.HttpMethod, request.Url?.PathAndQuery);
            
            switch (request.Url?.AbsolutePath)
            {
                case "/":
                case "/index.html":
                    await ServeIndexHtml(response);
                    break;
                    
                case "/api/metrics":
                    await ServeMetricsApi(response);
                    break;
                    
                case "/api/health":
                    await ServeHealthApi(response);
                    break;
                    
                default:
                    response.StatusCode = 404;
                    await WriteResponse(response, "Not Found");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
            context.Response.StatusCode = 500;
            await WriteResponse(context.Response, "Internal Server Error");
        }
        finally
        {
            context.Response.Close();
        }
    }
    
    private async Task ServeIndexHtml(HttpListenerResponse response)
    {
        response.ContentType = "text/html; charset=utf-8";
        
        var html = await File.ReadAllTextAsync("wwwroot/index.html");
        await WriteResponse(response, html);
    }
    
    private async Task ServeMetricsApi(HttpListenerResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        
        var stats = _metricsCollector.GetRealTimeStats();
        var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
        
        await WriteResponse(response, json);
    }
    
    private async Task ServeHealthApi(HttpListenerResponse response)
    {
        response.ContentType = "application/json; charset=utf-8";
        
        var health = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            uptime = Environment.TickCount / 1000.0
        };
        
        var json = JsonSerializer.Serialize(health);
        await WriteResponse(response, json);
    }
    
    private async Task WriteResponse(HttpListenerResponse response, string content)
    {
        var buffer = System.Text.Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }
    
    public override void Dispose()
    {
        _listener?.Close();
        base.Dispose();
    }
} 