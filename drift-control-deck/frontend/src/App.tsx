import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import axios from 'axios';

function App() {
  const [nodes, setNodes] = useState(1);
  const { data: metrics } = useQuery({
    queryKey: ['metrics'],
    queryFn: async () => {
      const res = await axios.get('/api/metrics');
      return res.data as any[];
    },
    refetchInterval: 2000
  });

  const launch = async () => {
    await axios.post('/api/launch', { nodes });
  };

  return (
    <div className="container mx-auto p-4">
      <h1 className="text-2xl font-bold mb-4">🚀 DriftNet Control Deck</h1>
      <div className="mb-4 p-4 bg-gray-100 rounded">
        <label className="block font-semibold mb-2">Network Nodes: {nodes}</label>
        <input 
          type="range" 
          min={1} 
          max={100} 
          value={nodes} 
          onChange={e => setNodes(Number(e.target.value))} 
          className="w-full mb-2" 
        />
        <button 
          onClick={launch} 
          className="px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded font-semibold"
        >
          🚀 Launch / Update Network
        </button>
      </div>
      
      <div className="overflow-x-auto">
        <table className="min-w-full bg-white border border-gray-300 rounded-lg shadow">
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
            {metrics?.map((m, i) => (
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
      
      {!metrics || metrics.length === 0 ? (
        <div className="text-center py-8 text-gray-500">
          <p>🔄 Waiting for network data...</p>
          <p className="text-sm">Launch nodes to see metrics</p>
        </div>
      ) : (
        <div className="mt-4 p-4 bg-green-50 rounded">
          <p className="text-sm text-green-800">
            ✅ Active nodes: {metrics.length} | 
            Total received: {metrics.reduce((sum, m) => sum + (m.chunks || 0), 0)} | 
            Total forwarded: {metrics.reduce((sum, m) => sum + (m.forwarded || 0), 0)} |
            Total circulating: {metrics.reduce((sum, m) => sum + (m.circulating || 0), 0)}
          </p>
        </div>
      )}
    </div>
  );
}

export default App; 