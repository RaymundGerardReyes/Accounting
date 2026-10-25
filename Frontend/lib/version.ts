/**
 * R.G.E Accounting Platform - Single Source of Version & Line Identity
 *
 * Versions are injected at build time via Next.js public environment variables:
 * - NEXT_PUBLIC_APP_VERSION: SemVer tag (e.g. "0.1.0-preview.1" or "0.9.34")
 * - NEXT_PUBLIC_APP_LINE: Deploy line ("dotnet" or "python")
 * - NEXT_PUBLIC_GIT_SHA: Short commit hash (e.g. "abc1234")
 * - NEXT_PUBLIC_API_CONTRACT: Authoritative API contract version (e.g. "v1")
 * - NEXT_PUBLIC_BUILD_TIME: ISO 8601 build timestamp
 *
 * During local development without injection, defaults to 'dev' / 'local-dev'.
 */

export interface VersionInfo {
  version: string;
  line: 'dotnet' | 'python' | 'dev';
  gitSha: string;
  apiContract: string;
  buildTime: string;
  displayString: string;
}

export const APP_VERSION: string =
  process.env.NEXT_PUBLIC_APP_VERSION || '0.0.0-dev';

export const APP_LINE: 'dotnet' | 'python' | 'dev' =
  (process.env.NEXT_PUBLIC_APP_LINE as 'dotnet' | 'python') || 'dev';

export const GIT_SHA: string =
  process.env.NEXT_PUBLIC_GIT_SHA || 'local-dev';

export const API_CONTRACT_VERSION: string =
  process.env.NEXT_PUBLIC_API_CONTRACT || 'v1';

export const BUILD_TIME: string =
  process.env.NEXT_PUBLIC_BUILD_TIME || new Date(0).toISOString();

export const VERSION_INFO: VersionInfo = {
  version: APP_VERSION,
  line: APP_LINE,
  gitSha: GIT_SHA,
  apiContract: API_CONTRACT_VERSION,
  buildTime: BUILD_TIME,
  displayString: `${APP_LINE}/${APP_VERSION} (${GIT_SHA.substring(0, 7)})`,
};

export default VERSION_INFO;

