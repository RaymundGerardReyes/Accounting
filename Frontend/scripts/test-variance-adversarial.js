const fs = require('fs');
const path = require('path');
const React = require('react');
const ReactDOMServer = require('react-dom/server');
const ts = require('typescript');

// 1. Mock UI components
const mockComponent = (name) => {
  const Component = ({ children }) => React.createElement('div', { 'data-mock': name }, children);
  Component.displayName = name;
  return Component;
};

const mocks = {
  '@/components/ui/button': { Button: mockComponent('Button') },
  '@/components/ui/card': {
    Card: mockComponent('Card'),
    CardContent: mockComponent('CardContent'),
    CardDescription: mockComponent('CardDescription'),
    CardHeader: mockComponent('CardHeader'),
    CardTitle: mockComponent('CardTitle'),
  },
  'lucide-react': {
    AlertCircle: mockComponent('AlertCircle'),
    ArrowLeft: mockComponent('ArrowLeft'),
    Download: mockComponent('Download'),
    TrendingDown: mockComponent('TrendingDown'),
    TrendingUp: mockComponent('TrendingUp'),
  },
  'recharts': {
    Area: mockComponent('Area'),
    AreaChart: mockComponent('AreaChart'),
    Bar: mockComponent('Bar'),
    BarChart: mockComponent('BarChart'),
    CartesianGrid: mockComponent('CartesianGrid'),
    Legend: mockComponent('Legend'),
    ResponsiveContainer: mockComponent('ResponsiveContainer'),
    Tooltip: mockComponent('Tooltip'),
    XAxis: mockComponent('XAxis'),
    YAxis: mockComponent('YAxis'),
  },
  'react': React,
};

// 2. Read and compile variance-analysis.tsx
const sourcePath = path.resolve(__dirname, '../components/variance-analysis.tsx');
const tsxCode = fs.readFileSync(sourcePath, 'utf8');

const compiled = ts.transpileModule(tsxCode, {
  compilerOptions: {
    jsx: ts.JsxEmit.React,
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    esModuleInterop: true,
  },
});

// 3. Sandbox loader
const customRequire = (moduleName) => {
  if (mocks[moduleName]) return mocks[moduleName];
  if (moduleName.startsWith('.')) {
    return require(path.resolve(path.dirname(sourcePath), moduleName));
  }
  return require(moduleName);
};

const moduleObj = { exports: {} };
const fn = new Function('require', 'module', 'exports', 'React', compiled.outputText);
fn(customRequire, moduleObj, moduleObj.exports, React);

const VarianceAnalysis = moduleObj.exports.VarianceAnalysis;
if (typeof VarianceAnalysis !== 'function') {
  console.error('[FAIL] Could not export VarianceAnalysis component');
  process.exit(1);
}

// 4. Test scenarios
const scenarios = [
  { name: '1. Null initialData', props: { initialData: null } },
  { name: '2. Empty object initialData', props: { initialData: {} } },
  { name: '3. Undefined varianceData property', props: { initialData: { varianceData: undefined } } },
  { name: '4. Undefined variance_data property', props: { initialData: { variance_data: undefined } } },
  { name: '5. Empty varianceData array', props: { initialData: { varianceData: [] } } },
  { name: '6. Empty variance_data array', props: { initialData: { variance_data: [] } } },
  { name: '7. Raw array payload', props: { initialData: [] } },
  { name: '8. Raw array with missing element fields', props: { initialData: [{ category: 'Labor' }, { budget: 1000 }] } },
  { name: '9. Zero budget with actual cost (0 division guard)', props: { initialData: { variance_data: [{ category: 'Test', budget: 0, actual: 50 }] } } },
  { name: '10. Null and NaN properties inside elements', props: { initialData: { variance_data: [{ category: null, budget: null, actual: null, variancePercent: null }] } } },
  { name: '11. .NET snapshot schema (variances with budget_limit_minor)', props: { initialData: { variances: [{ department: 'Manufacturing', budget_limit_minor: 4500000, actual_total_minor: 4600000, variance_minor: -100000 }] } } },
  { name: '12. Full snake_case dataset from .NET 10', props: {
    initialData: {
      variance_data: [{ category: 'Labor', budget: 180000, actual: 185000, variance: -5000, variance_percent: -2.8 }],
      monthly_variance: [{ month: 'Jan', variance: -3000, percentage: -2.4, favorable: 0, unfavorable: 1 }],
      variance_reasons: [{ reason: 'Labor Rate', amount: -4500, type: 'unfavorable', impact: 'High' }],
      department_variance: [{ department: 'Manufacturing', budget: 450000, actual: 465000, variance: -15000, status: 'unfavorable' }]
    }
  }},
  { name: '13. Full camelCase dataset from Python FastAPI', props: {
    initialData: {
      varianceData: [{ category: 'Materials', budget: 150000, actual: 148000, variance: 2000, variancePercent: 1.3 }],
      monthlyVariance: [{ month: 'Feb', variance: 2500, percentage: 1.9, favorable: 1, unfavorable: 0 }],
      varianceReasons: [{ reason: 'Material Price', amount: 1800, type: 'favorable', impact: 'Medium' }],
      departmentVariance: [{ department: 'Assembly', budget: 300000, actual: 292000, variance: 8000, status: 'favorable' }]
    }
  }},
  { name: '14. Extreme negative values and huge counts', props: {
    initialData: {
      variance_data: [
        { category: 'Cat A', budget: -500, actual: -1000 },
        { category: 'Cat B', budget: 1000000000, actual: 999999999 }
      ]
    }
  }}
];

let failed = 0;
console.log('--- Running VarianceAnalysis Adversarial Scenarios ---');

for (const sc of scenarios) {
  try {
    const element = React.createElement(VarianceAnalysis, { onBack: () => {}, ...sc.props });
    const html = ReactDOMServer.renderToString(element);
    if (!html || typeof html !== 'string') {
      console.error(`[FAIL] ${sc.name}: Empty render string`);
      failed++;
    } else if (html.includes('NaN%') || html.includes('₱NaN')) {
      console.error(`[FAIL] ${sc.name}: Output contains NaN representation: ${html.slice(0, 150)}...`);
      failed++;
    } else {
      console.log(`[PASS] ${sc.name}`);
    }
  } catch (err) {
    console.error(`[FAIL] ${sc.name}: Threw exception -> ${err.message}`);
    failed++;
  }
}

console.log('----------------------------------------------------');
if (failed === 0) {
  console.log(`All ${scenarios.length} scenarios passed cleanly without exception or NaN corruption.`);
  process.exit(0);
} else {
  console.error(`${failed} / ${scenarios.length} scenarios failed.`);
  process.exit(1);
}

