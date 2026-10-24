import path from "path"
import fs from "fs"

// Windows Node v24 EPERM workaround for Next.js async realpath resolution
if (fs.promises && fs.promises.realpath) {
  const origRealpath = fs.promises.realpath
  fs.promises.realpath = async (p, opts) => {
    try {
      return await origRealpath(p, opts)
    } catch (err) {
      if (err && (err.code === "EPERM" || err.code === "EACCES")) {
        return fs.realpathSync(p)
      }
      throw err
    }
  }
}

// Load authoritative root monorepo .env (E:\Accounting\.env) into process.env if present
const candidatePaths = [
  path.resolve(process.cwd(), "..", ".env"),
  path.resolve(process.cwd(), ".env"),
]
const rootEnvPath = candidatePaths.find((p) => fs.existsSync(p))
if (rootEnvPath) {
  try {
    const envContent = fs.readFileSync(rootEnvPath, "utf-8")
    for (const line of envContent.split(/\r?\n/)) {
      const trimmed = line.trim()
      if (trimmed && !trimmed.startsWith("#") && trimmed.includes("=")) {
        const [key, ...rest] = trimmed.split("=")
        const k = key.trim()
        const v = rest.join("=").trim().replace(/^["']|["']$/g, "")
        if (!process.env[k]) {
          process.env[k] = v
        }
      }
    }
  } catch (err) {
    console.warn("Notice: Could not parse root .env in Next.js config:", err)
  }
}

/** @type {import('next').NextConfig} */
const nextConfig = {
  typescript: {
    ignoreBuildErrors: true,
  },
  eslint: {
    ignoreDuringBuilds: true,
  },
  images: {
    unoptimized: true,
  },
  webpack: (config) => {
    config.resolve.alias = {
      ...config.resolve.alias,
      "@": path.resolve(process.cwd()),
    }
    return config
  },
  experimental: {
    optimizePackageImports: ["lucide-react", "recharts"],
    webpackBuildWorker: false,
    cpus: 1,
  },
}

export default nextConfig
