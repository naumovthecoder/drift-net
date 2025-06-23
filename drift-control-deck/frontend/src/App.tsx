import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import axios from 'axios';

function App() {
  const [nodes, setNodes] = useState(1);
  const { data: metrics } = useQuery({
    queryKey: ['metrics'],
    queryFn: async () => {
      const res = await axios.get('/api/metrics');
      return res.data as any;
    },
    refetchInterval: 2000
  });

  const launch = async () => {
    await axios.post('/api/launch', { nodes });
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'healthy': return 'bg-green-100 text-green-800';
      case 'degraded': return 'bg-yellow-100 text-yellow-800';
      case 'critical': return 'bg-red-100 text-red-800';
      case 'excellent': return 'bg-emerald-100 text-emerald-800';
      case 'good': return 'bg-blue-100 text-blue-800';
      case 'fair': return 'bg-orange-100 text-orange-800';
      case 'poor': return 'bg-red-100 text-red-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'healthy': return '✅';
      case 'degraded': return '⚠️';
      case 'critical': return '❌';
      case 'excellent': return '🌟';
      case 'good': return '👍';
      case 'fair': return '👌';
      case 'poor': return '👎';
      default: return '❓';
    }
  };

  const getHealthBarColor = (score: number) => {
    if (score >= 80) return 'bg-green-500';
    if (score >= 60) return 'bg-blue-500';
    if (score >= 40) return 'bg-yellow-500';
    if (score >= 20) return 'bg-orange-500';
    return 'bg-red-500';
  };

  return (
    <div className="container mx-auto p-4 space-y-6">
      <h1 className="text-3xl font-bold mb-6">🚀 DriftNet Control Deck</h1>
      
      {/* Control Panel */}
      <div className="bg-gray-100 rounded-lg p-4">
        <label className="block font-semibold mb-2">Network Nodes: {nodes}</label>
        <input 
          type="range" 
          min={1} 
          max={100} 
          value={nodes} 
          onChange={e => setNodes(Number(e.target.value))} 
          className="w-full mb-3" 
        />
        <button 
          onClick={launch} 
          className="px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded font-semibold"
        >
          🚀 Launch / Update Network
        </button>
      </div>

      {/* Network Health Dashboard */}
      {metrics?.networkHealth && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">🏥 Network Health</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-4">
            <div className="text-center p-4 bg-gradient-to-br from-blue-50 to-blue-100 rounded-lg">
              <div className="text-3xl font-bold text-blue-600">{metrics.networkHealth.score}</div>
              <div className="text-sm text-gray-600">Health Score</div>
              <div className="w-full bg-gray-200 rounded-full h-2 mt-2">
                <div 
                  className={`h-2 rounded-full ${getHealthBarColor(metrics.networkHealth.score)}`}
                  style={{ width: `${metrics.networkHealth.score}%` }}
                ></div>
              </div>
            </div>
            <div className="text-center p-4 bg-gradient-to-br from-green-50 to-green-100 rounded-lg">
              <div className="text-2xl font-bold text-green-600">{metrics.networkHealth.activeNodes}/{metrics.networkHealth.totalNodes}</div>
              <div className="text-sm text-gray-600">Active Nodes</div>
              <span className={`inline-flex items-center px-2 py-1 rounded-full text-xs font-medium ${getStatusColor(metrics.networkHealth.status)}`}>
                {getStatusIcon(metrics.networkHealth.status)} {metrics.networkHealth.status}
              </span>
            </div>
            <div className="text-center p-4 bg-gradient-to-br from-purple-50 to-purple-100 rounded-lg">
              <div className="text-2xl font-bold text-purple-600">{metrics.networkHealth.networkLoad}</div>
              <div className="text-sm text-gray-600">Network Load</div>
              <div className="text-xs text-gray-500 mt-1">Active Chunks</div>
            </div>
            <div className="text-center p-4 bg-gradient-to-br from-orange-50 to-orange-100 rounded-lg">
              <div className="text-2xl font-bold text-orange-600">{Math.round(metrics.networkHealth.redundancyLevel)}%</div>
              <div className="text-sm text-gray-600">Redundancy</div>
              <div className="text-xs text-gray-500 mt-1">Error Rate: {metrics.networkHealth.avgErrorRate}%</div>
            </div>
          </div>
        </div>
      )}

      {/* Hot Chunks Analytics */}
      {metrics?.hotChunks && metrics.hotChunks.topChunks?.length > 0 && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">🔥 Hot Chunks Analytics</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-4">
            <div className="text-center p-3 bg-red-50 rounded-lg">
              <div className="text-2xl font-bold text-red-600">{metrics.hotChunks.totalTransfers}</div>
              <div className="text-sm text-gray-600">Total Transfers</div>
            </div>
            <div className="text-center p-3 bg-orange-50 rounded-lg">
              <div className="text-2xl font-bold text-orange-600">{metrics.hotChunks.uniqueChunks}</div>
              <div className="text-sm text-gray-600">Unique Chunks</div>
            </div>
            <div className="text-center p-3 bg-yellow-50 rounded-lg">
              <div className="text-2xl font-bold text-yellow-600">{Math.round(metrics.hotChunks.avgTransfersPerChunk)}</div>
              <div className="text-sm text-gray-600">Avg Transfers</div>
            </div>
            <div className="text-center p-3 bg-pink-50 rounded-lg">
              <div className="text-2xl font-bold text-pink-600">{metrics.hotChunks.topChunks[0]?.transferCount || 0}</div>
              <div className="text-sm text-gray-600">Top Chunk</div>
            </div>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">🔥 Rank</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Chunk ID</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Transfers</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Popularity</th>
                  <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Last Transfer</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {metrics.hotChunks.topChunks.map((chunk: any, i: number) => (
                  <tr key={chunk.id} className={i % 2 === 0 ? "bg-white" : "bg-gray-50"}>
                    <td className="px-4 py-2 whitespace-nowrap text-sm font-medium">
                      <span className="text-xl">{i === 0 ? '🏆' : i === 1 ? '🥈' : i === 2 ? '🥉' : `#${i + 1}`}</span>
                    </td>
                    <td className="px-4 py-2 whitespace-nowrap text-sm font-mono text-gray-900">{chunk.id}</td>
                    <td className="px-4 py-2 whitespace-nowrap text-sm">
                      <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800">
                        {chunk.transferCount}
                      </span>
                    </td>
                    <td className="px-4 py-2 whitespace-nowrap text-sm">
                      <div className="w-full bg-gray-200 rounded-full h-2">
                        <div 
                          className="bg-gradient-to-r from-red-400 to-red-600 h-2 rounded-full"
                          style={{ width: `${Math.min(100, chunk.popularityScore * 10)}%` }}
                        ></div>
                      </div>
                    </td>
                    <td className="px-4 py-2 whitespace-nowrap text-xs text-gray-500">
                      {new Date(chunk.lastTransfer).toLocaleTimeString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Node Rankings */}
      {metrics?.nodeRankings && metrics.nodeRankings.length > 0 && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">🏆 Node Performance Rankings</h2>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {metrics.nodeRankings.map((node: any) => (
              <div key={node.nodeId} className={`border rounded-lg p-4 ${node.rank === 1 ? 'border-yellow-300 bg-yellow-50' : node.rank === 2 ? 'border-gray-300 bg-gray-50' : node.rank === 3 ? 'border-orange-300 bg-orange-50' : 'border-gray-200'}`}>
                <div className="flex items-center justify-between mb-2">
                  <h3 className="font-semibold text-gray-900">{node.nodeId}</h3>
                  <span className="text-2xl">{node.rank === 1 ? '🏆' : node.rank === 2 ? '🥈' : node.rank === 3 ? '🥉' : '⭐'}</span>
                </div>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-gray-600">Contribution Score:</span>
                    <span className="font-bold text-blue-600">{node.contributionScore}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600">Total Activity:</span>
                    <span className="font-mono">{node.totalActivity}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600">Efficiency:</span>
                    <span className="font-mono">{Math.round(node.efficiency)}%</span>
                  </div>
                  <div className="text-center mt-2">
                    <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                      {node.badge}
                    </span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Traffic Patterns */}
      {metrics?.trafficPatterns && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">📊 Traffic Patterns</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-4">
            <div className="text-center p-3 bg-indigo-50 rounded-lg">
              <div className="text-2xl font-bold text-indigo-600">{metrics.trafficPatterns.peakHour}:00</div>
              <div className="text-sm text-gray-600">Peak Hour</div>
            </div>
            <div className="text-center p-3 bg-cyan-50 rounded-lg">
              <div className="text-2xl font-bold text-cyan-600">{metrics.trafficPatterns.avgThroughput}</div>
              <div className="text-sm text-gray-600">Avg Throughput</div>
            </div>
            <div className="text-center p-3 bg-emerald-50 rounded-lg">
              <div className="text-lg font-bold text-emerald-600">{metrics.trafficPatterns.trendDirection}</div>
              <div className="text-sm text-gray-600">Trend</div>
            </div>
            <div className="text-center p-3 bg-violet-50 rounded-lg">
              <div className="text-2xl font-bold text-violet-600">{metrics.trafficPatterns.hourlyPattern?.length || 0}</div>
              <div className="text-sm text-gray-600">Active Hours</div>
            </div>
          </div>
        </div>
      )}

      {/* Files Status */}
      {metrics?.files && metrics.files.length > 0 && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">📁 Files Status</h2>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {metrics.files.map((file: any, i: number) => (
              <div key={i} className="border rounded-lg p-4">
                <div className="flex items-center justify-between mb-2">
                  <h3 className="font-semibold text-gray-900">File #{i + 1}</h3>
                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusColor(file.status)}`}>
                    {getStatusIcon(file.status)} {file.status}
                  </span>
                </div>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-gray-600">Chunks:</span>
                    <span className="font-mono">{file.activeChunks}/{file.totalChunks}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600">Recovery Rate:</span>
                    <span className="font-mono">{file.recoveryRate}%</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-600">Avg TTL:</span>
                    <span className="font-mono">{Math.round(file.avgTTL)}</span>
                  </div>
                  <div className="w-full bg-gray-200 rounded-full h-2">
                    <div 
                      className={`h-2 rounded-full ${
                        file.recoveryRate >= 90 ? 'bg-green-500' : 
                        file.recoveryRate >= 50 ? 'bg-yellow-500' : 'bg-red-500'
                      }`}
                      style={{ width: `${file.recoveryRate}%` }}
                    ></div>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Network Overview */}
      {metrics?.chunks && (
        <div className="bg-white rounded-lg shadow p-4">
          <h2 className="text-xl font-semibold mb-4">🌐 Network Overview</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="text-center p-4 bg-blue-50 rounded-lg">
              <div className="text-2xl font-bold text-blue-600">{metrics.chunks.total}</div>
              <div className="text-sm text-gray-600">Total Chunks</div>
            </div>
            <div className="text-center p-4 bg-green-50 rounded-lg">
              <div className="text-2xl font-bold text-green-600">{metrics.chunks.active}</div>
              <div className="text-sm text-gray-600">Active Chunks</div>
            </div>
            <div className="text-center p-4 bg-yellow-50 rounded-lg">
              <div className="text-2xl font-bold text-yellow-600">{metrics.chunks.inactive}</div>
              <div className="text-sm text-gray-600">Inactive Chunks</div>
            </div>
            <div className="text-center p-4 bg-purple-50 rounded-lg">
              <div className="text-2xl font-bold text-purple-600">{Math.round(metrics.chunks.avgTTL || 0)}</div>
              <div className="text-sm text-gray-600">Avg TTL</div>
            </div>
          </div>
        </div>
      )}
      
      {/* Enhanced Nodes Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b">
          <h2 className="text-xl font-semibold">⚡ Network Nodes</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Node ID</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">📥 Received</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">📤 Forwarded</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🔄 Circulating</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">⚡ Avg TTL</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🚫 Loops</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">💾 CPU</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🧠 Memory</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🌐 Bandwidth</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">⏱️ Uptime</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🎯 Score</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {metrics?.nodes?.map((m: any, i: number) => (
                <tr key={m.id} className={i % 2 === 0 ? "bg-white" : "bg-gray-50"}>
                  <td className="px-4 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {m.id.replace('backend-driftnode-', 'node-')}
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                      {m.chunks || 0}
                    </span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                      {m.forwarded || 0}
                    </span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-purple-100 text-purple-800">
                      {m.circulating || 0}
                    </span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
                      {m.avgTTL || 0}
                    </span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
                      {m.loopsDropped || 0}
                    </span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="text-xs font-mono">{m.cpu || 0}%</span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="text-xs font-mono">{m.mem || 0}MB</span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="text-xs font-mono">{Math.round(m.bandwidth || 0)}MB/s</span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className="text-xs font-mono">{m.uptime || '0m'}</span>
                  </td>
                  <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-900">
                    <span className={`inline-flex items-center px-2 py-1 rounded-full text-xs font-medium ${
                      (m.contributionScore || 0) > 60 ? 'bg-green-100 text-green-800' :
                      (m.contributionScore || 0) > 30 ? 'bg-yellow-100 text-yellow-800' : 'bg-red-100 text-red-800'
                    }`}>
                      {Math.round(m.contributionScore || 0)}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      
      {/* Enhanced Summary Stats */}
      {metrics?.nodes && metrics.nodes.length > 0 && (
        <div className="bg-gradient-to-r from-green-50 to-blue-50 rounded-lg p-4">
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-center">
            <div>
              <div className="text-2xl font-bold text-green-600">{metrics.nodes.length}</div>
              <div className="text-sm text-gray-600">Active Nodes</div>
            </div>
            <div>
              <div className="text-2xl font-bold text-blue-600">{metrics.nodes.reduce((sum: number, m: any) => sum + (m.chunks || 0), 0)}</div>
              <div className="text-sm text-gray-600">Total Received</div>
            </div>
            <div>
              <div className="text-2xl font-bold text-purple-600">{metrics.nodes.reduce((sum: number, m: any) => sum + (m.forwarded || 0), 0)}</div>
              <div className="text-sm text-gray-600">Total Forwarded</div>
            </div>
            <div>
              <div className="text-2xl font-bold text-orange-600">{metrics.files?.length || 0}</div>
              <div className="text-sm text-gray-600">Total Files</div>
            </div>
          </div>
          <div className="mt-4 text-center">
            <p className="text-sm text-gray-700">
              🔥 Network Traffic: {metrics.nodes.reduce((sum: number, m: any) => sum + (m.circulating || 0), 0) || 0} circulating | 
              💪 Avg Bandwidth: {Math.round(metrics.nodes.reduce((sum: number, m: any) => sum + (m.bandwidth || 0), 0) / metrics.nodes.length || 0)}MB/s |
              🎯 Top Score: {Math.max(...metrics.nodes.map((m: any) => m.contributionScore || 0))}
            </p>
          </div>
        </div>
      )}

      {/* No Data State */}
      {(!metrics?.nodes || metrics.nodes.length === 0) && (
        <div className="text-center py-12 text-gray-500">
          <div className="text-6xl mb-4">🔄</div>
          <p className="text-lg">Waiting for network data...</p>
          <p className="text-sm mt-2">Launch the network to see real-time metrics</p>
        </div>
      )}
    </div>
  );
}

export default App; 