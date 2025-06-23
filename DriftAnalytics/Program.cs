using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Добавляем CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles();

// Главная страница с аналитикой
app.MapGet("/", async (HttpContext context) =>
{
    var html = @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>DriftNet Analytics</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>
    <style>
        .card { margin-bottom: 1rem; }
        .metric-card { text-align: center; }
        .metric-value { font-size: 2rem; font-weight: bold; }
    </style>
</head>
<body>
    <div class='container-fluid mt-4'>
        <h1 class='text-center mb-4'>🌊 DriftNet Analytics Dashboard</h1>
        
        <div class='row' id='metrics'>
            <div class='col-md-3'>
                <div class='card bg-primary text-white metric-card'>
                    <div class='card-body'>
                        <h5>Total Nodes</h5>
                        <div class='metric-value' id='totalNodes'>-</div>
                        <small>Active: <span id='activeNodes'>-</span></small>
                    </div>
                </div>
            </div>
            <div class='col-md-3'>
                <div class='card bg-success text-white metric-card'>
                    <div class='card-body'>
                        <h5>Chunks in Flight</h5>
                        <div class='metric-value' id='totalChunks'>-</div>
                        <small><span id='totalBytes'>-</span></small>
                    </div>
                </div>
            </div>
            <div class='col-md-3'>
                <div class='card bg-info text-white metric-card'>
                    <div class='card-body'>
                        <h5>Avg TTL</h5>
                        <div class='metric-value' id='avgTTL'>-</div>
                        <small>Time to Live</small>
                    </div>
                </div>
            </div>
            <div class='col-md-3'>
                <div class='card bg-warning text-white metric-card'>
                    <div class='card-body'>
                        <h5>Last Updated</h5>
                        <div class='metric-value' id='lastUpdated'>-</div>
                        <small>Real-time</small>
                    </div>
                </div>
            </div>
        </div>

        <div class='row'>
            <div class='col-12'>
                <div class='card'>
                    <div class='card-header'>
                        <h5>Node Activity</h5>
                    </div>
                    <div class='card-body'>
                        <div class='table-responsive'>
                            <table class='table table-striped' id='nodesTable'>
                                <thead>
                                    <tr>
                                        <th>Node ID</th>
                                        <th>Status</th>
                                        <th>Chunks Received</th>
                                        <th>Chunks Forwarded</th>
                                        <th>Last Activity</th>
                                    </tr>
                                </thead>
                                <tbody id='nodesBody'>
                                </tbody>
                            </table>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js'></script>
    <script>
        function updateMetrics() {
            fetch('/api/metrics')
                .then(response => response.json())
                .then(data => {
                    document.getElementById('totalNodes').textContent = data.totalNodes || 0;
                    document.getElementById('activeNodes').textContent = data.activeNodes || 0;
                    document.getElementById('totalChunks').textContent = data.totalChunks || 0;
                    document.getElementById('totalBytes').textContent = formatBytes(data.totalBytes || 0);
                    document.getElementById('avgTTL').textContent = (data.averageTTL || 0).toFixed(1);
                    document.getElementById('lastUpdated').textContent = new Date().toLocaleTimeString();
                    
                    updateNodesTable(data.nodes || []);
                })
                .catch(error => {
                    console.error('Error fetching metrics:', error);
                });
        }

        function updateNodesTable(nodes) {
            const tbody = document.getElementById('nodesBody');
            tbody.innerHTML = '';
            
            nodes.forEach(node => {
                const row = document.createElement('tr');
                row.innerHTML = `
                    <td><span class='badge bg-primary'>${node.nodeId}</span></td>
                    <td><span class='badge ${getStatusBadgeClass(node.status)}'>${node.status}</span></td>
                    <td>${node.chunksReceived || 0}</td>
                    <td>${node.chunksForwarded || 0}</td>
                    <td>${new Date(node.lastActivity).toLocaleTimeString()}</td>
                `;
                tbody.appendChild(row);
            });
        }

        function getStatusBadgeClass(status) {
            switch(status.toLowerCase()) {
                case 'running': return 'bg-success';
                case 'exited': return 'bg-danger';
                case 'created': return 'bg-warning';
                default: return 'bg-secondary';
            }
        }

        function formatBytes(bytes) {
            if (bytes === 0) return '0 B';
            const k = 1024;
            const sizes = ['B', 'KB', 'MB', 'GB'];
            const i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        }

        // Обновляем метрики каждые 2 секунды
        setInterval(updateMetrics, 2000);
        updateMetrics(); // Первоначальная загрузка
    </script>
</body>
</html>";

    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync(html);
});

// API endpoint для получения метрик
app.MapGet("/api/metrics", async (HttpContext context) =>
{
    try
    {
        var metrics = await CollectDockerMetrics();
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(metrics));
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
    }
});

// Health check endpoint
app.MapGet("/api/health", () =>
{
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
});

async Task<object> CollectDockerMetrics()
{
    // Простая симуляция метрик для демонстрации
    var random = new Random();

    return new
    {
        totalNodes = 20,
        activeNodes = 20,
        totalChunks = random.Next(50, 200),
        totalBytes = random.Next(10000000, 50000000),
        averageTTL = random.Next(8000, 10000),
        lastUpdated = DateTime.UtcNow,
        nodes = Enumerable.Range(1, 20).Select(i => new
        {
            nodeId = $"node-{i}",
            status = "running",
            chunksReceived = random.Next(10, 50),
            chunksForwarded = random.Next(10, 50),
            lastActivity = DateTime.UtcNow.AddSeconds(-random.Next(0, 60))
        }).ToArray()
    };
}

Console.WriteLine("🌊 DriftNet Analytics starting...");
Console.WriteLine("📊 Web interface: http://localhost:8080");
Console.WriteLine("🔌 API endpoint: http://localhost:8080/api/metrics");

app.Run();