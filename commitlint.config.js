module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      [
        'feat',
        'fix',
        'perf',
        'refactor',
        'docs',
        'test',
        'build',
        'ci',
        'chore',
        'revert',
      ],
    ],
    'scope-enum': [
      2,
      'always',
      [
        'web',
        'payments',
        'receipts',
        'finance',
        'identity',
        'audit',
        'costalloc',
        'documents',
        'integrations',
        'db',
        'deploy',
        'release',
        'deps',
      ],
    ],
    'header-max-length': [2, 'always', 100],
    'body-max-line-length': [2, 'always', 100],
  },
};

