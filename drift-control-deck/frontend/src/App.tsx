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
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'healthy': return '✅';
      case 'degraded': return '⚠️';
      case 'critical': return '❌';
      default: return '❓';
    }
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
      
      {/* Nodes Table */}
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
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">🚫 Loops Dropped</th>
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
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      
      {/* Summary Stats */}
      {metrics?.nodes && metrics.nodes.length > 0 && (
        <div className="bg-green-50 rounded-lg p-4">
          <p className="text-sm text-green-800">
            ✅ Active nodes: {metrics.nodes.length} | 
            Total received: {metrics.nodes.reduce((sum: number, m: any) => sum + (m.chunks || 0), 0)} | 
            Total forwarded: {metrics.nodes.reduce((sum: number, m: any) => sum + (m.forwarded || 0), 0)} |
            Total circulating: {metrics.nodes.reduce((sum: number, m: any) => sum + (m.circulating || 0), 0)} |
            Total files: {metrics.files?.length || 0}
          </p>
        </div>
      )}

      {/* No Data State */}
      {(!metrics?.nodes || metrics.nodes.length === 0) && (
        <div className="text-center py-12 text-gray-500">
          <div className="text-6xl mb-4">🔄</div>
          <p className="text-lg">Waiting for network data...</p>
          <p className="text-sm">Launch nodes to see metrics</p>
        </div>
      )}
    </div>
  );
}

export default App; 