// Turns coverage/coverage-summary.json (Vitest json-summary reporter) into a Markdown table.
// Usage: node scripts/coverage-summary.mjs >> "$GITHUB_STEP_SUMMARY"
import { readFileSync } from 'node:fs';

const summaryPath = new URL('../coverage/coverage-summary.json', import.meta.url);
const { total } = JSON.parse(readFileSync(summaryPath, 'utf8'));

const metrics = ['lines', 'statements', 'functions', 'branches'];
const rows = metrics.map((metric) => {
    const { pct, covered, total: count } = total[metric];
    return `| ${metric[0].toUpperCase()}${metric.slice(1)} | ${pct}% | ${covered} of ${count} |`;
});

console.log(
    [
        '## Web coverage (`web/src`)',
        '',
        '| Metric | Coverage | Covered |',
        '| --- | ---: | ---: |',
        ...rows,
        '',
    ].join('\n'),
);
