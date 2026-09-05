import { access, mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { validateMarketNewsFile } from "./validate-market-news.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..");
const outputPath = path.join(repoRoot, "frontend", "public", "data", "market_news.json");
const projectPath = path.join(repoRoot, "backend", "SmartMoney.ExternalContext.Job", "SmartMoney.ExternalContext.Job.csproj");

async function readValidExistingDocument() {
  try {
    const contents = await readFile(outputPath, "utf8");
    await validateMarketNewsFile(outputPath);
    return contents;
  } catch {
    return null;
  }
}

function run(command, argumentsList) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, argumentsList, { cwd: repoRoot, stdio: "inherit" });
    child.on("error", reject);
    child.on("close", (code) => code === 0 ? resolve() : reject(new Error(`${command} exited with code ${code}`)));
  });
}

async function restoreLastKnownGood(contents) {
  if (contents === null) return;
  await mkdir(path.dirname(outputPath), { recursive: true });
  const temporaryPath = `${outputPath}.restore-${process.pid}`;
  await writeFile(temporaryPath, contents, "utf8");
  await rename(temporaryPath, outputPath);
  console.warn("[market-news] Preserved the previous valid last-known-good document.");
}

async function writeJsonAtomically(document) {
  await mkdir(path.dirname(outputPath), { recursive: true });
  const temporaryPath = `${outputPath}.write-${process.pid}`;
  await writeFile(temporaryPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  await rename(temporaryPath, outputPath);
}

async function main() {
  const previousDocument = await readValidExistingDocument();
  console.log(previousDocument === null
    ? "[market-news] No valid existing document found."
    : "[market-news] Valid last-known-good document found.");

  try {
    if (process.argv.includes("--simulate-failure")) {
      throw new Error("Simulated External Context job failure.");
    }

    if (process.argv.includes("--simulate-zero")) {
      await writeJsonAtomically({
        generated_at_utc: new Date().toISOString(),
        lookback_hours: 168,
        items: [],
      });
      const document = await validateMarketNewsFile(outputPath);
      console.log(`[market-news] Generated and validated ${document.items.length} item(s).`);
      return;
    }

    await access(projectPath);
    await run("dotnet", ["run", "-c", "Release", "--project", projectPath, "--", "--enabled", "true", "--lookback-hours", "168", "--max-output-items", "5", "--output", outputPath]);
    const document = await validateMarketNewsFile(outputPath);
    console.log(`[market-news] Generated and validated ${document.items.length} item(s).`);
  } catch (error) {
    console.warn(`[market-news] WARNING: External Context refresh failed: ${error instanceof Error ? error.message : error}`);
    await restoreLastKnownGood(previousDocument);
  }
}

await main();