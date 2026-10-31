const fs = require('fs');
const path = require('path');
const vm = require('vm');
const React = require('react');
const ReactDOMServer = require('react-dom/server');
const ts = require('typescript');

// 1. Mock dependencies
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
    TrendingUp: mockComponent('TrendingUp'),
  },
  'recharts': {
    Bar: mockComponent('Bar'),
    BarChart: mockComponent('BarChart'),
    CartesianGrid: mockComponent('CartesianGrid'),
    Legend: mockComponent('Legend'),
    Line: mockComponent('Line'),
    LineChart: mockComponent('LineChart'),
    ResponsiveContainer: mockComponent('ResponsiveContainer'),
    Scatter: mockComponent('Scatter'),
    ScatterChart: mockComponent('ScatterChart'),
    Tooltip: mockComponent('Tooltip'),
    XAxis: mockComponent('XAxis'),
    YAxis: mockComponent('YAxis'),
  },
  'react': React,
};

// 2. Read and compile product-line-analysis.tsx
const sourcePath = path.resolve(__dirname, '../components/product-line-analysis.tsx');
const tsxCode = fs.readFileSync(sourcePath, 'utf8');

const compiled = ts.transpileModule(tsxCode, {
  compilerOptions: {
    jsx: ts.JsxEmit.React,
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    esModuleInterop: true,
  },
});

// 3. Create sandbox execution environment
const customRequire = (moduleName) => {
  if (mocks[moduleName]) return mocks[moduleName];
  if (moduleName === 'react/jsx-runtime') {
    return {
      jsx: React.createElement,
      jsxs: React.createElement,
      Fragment: React.Fragment,
    };
  }
  return require(moduleName);
};

const moduleObj = { exports: {} };
const context = vm.createContext({
  require: customRequire,
  module: moduleObj,
  exports: moduleObj.exports,
  React: React,
  console: console,
  process: process,
  Buffer: Buffer,
  setTimeout: setTimeout,
  clearTimeout: clearTimeout,
});

vm.runInContext(compiled.outputText, context);
const { ProductLineAnalysis } = moduleObj.exports;

if (typeof ProductLineAnalysis !== 'function') {
  console.error('FATAL: ProductLineAnalysis was not exported as a function!');
  process.exit(1);
}

// 4. Define all Edge Cases
const testCases = [
  // Required Edge Cases 1-8
  { id: 1, name: 'data = undefined', data: undefined, expectRenderSubstring: 'Loading product analysis' },
  { id: 2, name: 'data = null', data: null, expectRenderSubstring: 'No product data available' },
  { id: 3, name: 'data = {}', data: {}, expectRenderSubstring: 'No active product lines found' },
  { id: 4, name: 'data = { productData: undefined }', data: { productData: undefined }, expectRenderSubstring: 'No active product lines found' },
  { id: 5, name: 'data = { productData: null }', data: { productData: null }, expectRenderSubstring: 'No active product lines found' },
  { id: 6, name: 'data = []', data: [], expectRenderSubstring: 'No active product lines found' },
  { id: 7, name: 'data = [{}]', data: [{}], expectRenderSubstring: 'Product 1' },
  { id: 8, name: 'data = [{ product: "X" }]', data: [{ product: 'X' }], expectRenderSubstring: 'X' },

  // Additional Adversarial Edge Cases
  { id: 11, name: 'data = { productData: "string instead of array" }', data: { productData: 'not_an_array' }, expectRenderSubstring: 'No active product lines found' },
  { id: 12, name: 'data = { productData: 12345 }', data: { productData: 12345 }, expectRenderSubstring: 'No active product lines found' },
  { id: 13, name: 'data = { productData: [null, undefined, false, 0, "", {}] }', data: { productData: [null, undefined, false, 0, "", {}] }, expectRenderSubstring: 'Product 1' },
  { id: 14, name: 'data = { productData: [{ volume: NaN, profit: NaN, margin: NaN, unitCost: NaN }] }', data: { productData: [{ volume: NaN, profit: NaN, margin: NaN, unitCost: NaN }] }, expectRenderSubstring: 'Product 1' },
  { id: 15, name: 'data = { productData: [{ volume: -500, profit: -1000, margin: -25, unitCost: -10 }] }', data: { productData: [{ volume: -500, profit: -1000, margin: -25, unitCost: -10 }] }, expectRenderSubstring: 'Product 1' },
  { id: 16, name: 'data = { products: [{ name: "Widget", unitsProduced: 250, unitDirectCostMinor: 45000 }] }', data: { products: [{ name: "Widget", unitsProduced: 250, unitDirectCostMinor: 45000 }] }, expectRenderSubstring: 'Widget' },
  { id: 17, name: 'data = [{ product: "Product A", unitCost: 100, volume: 50, margin: 40, revenue: 8000, profit: 3000 }]', data: [{ product: "Product A", unitCost: 100, volume: 50, margin: 40, revenue: 8000, profit: 3000 }], expectRenderSubstring: 'Product A' },
  { id: 18, name: 'data = "raw string response"', data: 'raw string response', expectRenderSubstring: 'No active product lines found' },
  { id: 19, name: 'data = 42', data: 42, expectRenderSubstring: 'No active product lines found' },
  { id: 20, name: 'data = { productData: [{ volume: Number.MAX_SAFE_INTEGER, profit: Number.MAX_SAFE_INTEGER, unitCost: 1e9 }] }', data: { productData: [{ volume: Number.MAX_SAFE_INTEGER, profit: Number.MAX_SAFE_INTEGER, unitCost: 1e9 }] }, expectRenderSubstring: 'Product 1' },
];

console.log('================================================================');
console.log(' EMPIRICAL STRESS TEST HARNESS: ProductLineAnalysis Runtime');
console.log('================================================================');

let passedCount = 0;
let failedCount = 0;
const failures = [];

for (const tc of testCases) {
  try {
    const element = React.createElement(ProductLineAnalysis, {
      initialData: tc.data,
      onBack: () => {},
    });

    const markup = ReactDOMServer.renderToStaticMarkup(element);

    // Requirement 9: avgMargin and avgUnitCost NEVER render "NaN" or "₱NaN"
    const hasNaN = markup.includes('NaN') || markup.includes('₱NaN');
    // Check unhandled reduce or crash markers
    const hasCrash = markup.includes('Cannot read properties') || markup.includes('TypeError');
    const hasExpectedSubstring = tc.expectRenderSubstring ? markup.includes(tc.expectRenderSubstring) : true;

    if (hasNaN) {
      throw new Error(`CRITICAL: Rendered markup contains 'NaN' or '₱NaN'! Markup snippet: ${markup.slice(0, 300)}`);
    }
    if (hasCrash) {
      throw new Error(`CRITICAL: Rendered markup contains crash message!`);
    }
    if (!hasExpectedSubstring) {
      throw new Error(`Markup missing expected substring "${tc.expectRenderSubstring}"`);
    }

    console.log(`[PASS] Case #${tc.id.toString().padStart(2, '0')}: ${tc.name}`);
    passedCount++;
  } catch (err) {
    console.error(`[FAIL] Case #${tc.id.toString().padStart(2, '0')}: ${tc.name}`);
    console.error(`       Error: ${err.message}`);
    failedCount++;
    failures.push({ case: tc, error: err.message });
  }
}

// 5. Test empty data specifically for Requirement 9
console.log('\n--- Checking Requirement 9: Empty Data NaN / ₱NaN Immunity ---');
const emptyDataVariations = [
  { label: 'data = null', data: null },
  { label: 'data = {}', data: {} },
  { label: 'data = []', data: [] },
  { label: 'data = { productData: [] }', data: { productData: [] } },
  { label: 'data = { productData: undefined }', data: { productData: undefined } },
  { label: 'data = { productData: null }', data: { productData: null } },
  { label: 'data = { products: [] }', data: { products: [] } },
  { label: 'data = { profitabilityData: [], trendData: [] }', data: { profitabilityData: [], trendData: [] } },
];

let r9Passed = true;
for (const variation of emptyDataVariations) {
  const element = React.createElement(ProductLineAnalysis, {
    initialData: variation.data,
    onBack: () => {},
  });
  const markup = ReactDOMServer.renderToStaticMarkup(element);
  if (markup.includes('NaN') || markup.includes('₱NaN')) {
    console.error(`[R9 FAIL] ${variation.label} rendered NaN or ₱NaN!`);
    r9Passed = false;
  } else {
    console.log(`[R9 OK]   ${variation.label.padEnd(45, ' ')} -> No NaN/₱NaN detected.`);
  }
}

console.log('================================================================');
console.log(`RESULTS: Total: ${testCases.length} | Passed: ${passedCount} | Failed: ${failedCount}`);
console.log(`Requirement 9 Immunity: ${r9Passed ? 'VERIFIED PASSED' : 'FAILED'}`);
console.log('================================================================');

if (failedCount > 0 || !r9Passed) {
  process.exit(1);
} else {
  console.log('ALL ADVERSARIAL STRESS TESTS COMPLETED SUCCESSFULLY.');
  process.exit(0);
}
