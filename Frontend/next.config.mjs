import path from "path"
import fs from "fs"

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
    ignoreBuildErrors: false,
  },
  images: {
    unoptimized: true,
  },
  experimental: {
    optimizePackageImports: ["lucide-react", "recharts"],
  },
}

export default nextConfig
