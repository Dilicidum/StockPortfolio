#!/usr/bin/env node

import { readdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const here = path.dirname(fileURLToPath(import.meta.url))
const localesRoot = path.resolve(here, '..', 'src', 'locales')

const LANGUAGES = ['en', 'uk']

function readNamespaceFiles(language) {
  const dir = path.join(localesRoot, language)
  let entries
  try {
    entries = readdirSync(dir)
  } catch {
    return []
  }
  return entries.filter((name) => name.endsWith('.json'))
}

function flatten(value, prefix, into) {
  if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
    for (const [key, child] of Object.entries(value)) {
      flatten(child, prefix ? `${prefix}.${key}` : key, into)
    }
    return
  }
  into.add(prefix)
}

function readKeySet(language) {
  const keys = new Set()

  for (const file of readNamespaceFiles(language)) {
    const namespace = file.slice(0, -'.json'.length)
    const fullPath = path.join(localesRoot, language, file)
    const raw = readFileSync(fullPath, 'utf8')

    let parsed
    try {
      parsed = JSON.parse(raw)
    } catch (error) {
      console.error(`Could not parse ${path.relative(process.cwd(), fullPath)}: ${error.message}`)
      process.exitCode = 1
      continue
    }

    const fileKeys = new Set()
    flatten(parsed, '', fileKeys)
    fileKeys.delete('')

    for (const key of fileKeys) keys.add(`${namespace}:${key}`)
  }

  return keys
}

const [en, uk] = LANGUAGES.map(readKeySet)

const missingFromUk = [...en].filter((key) => !uk.has(key)).sort()
const missingFromEn = [...uk].filter((key) => !en.has(key)).sort()

if (missingFromUk.length === 0 && missingFromEn.length === 0) {
  console.log(`Locale parity OK — en: ${en.size} keys, uk: ${uk.size} keys.`)
  process.exit(0)
}

if (missingFromUk.length > 0) {
  console.error(`Present in en, missing from uk (${missingFromUk.length}):`)
  for (const key of missingFromUk) console.error(`  ${key}`)
}

if (missingFromEn.length > 0) {
  console.error(`Present in uk, missing from en (${missingFromEn.length}):`)
  for (const key of missingFromEn) console.error(`  ${key}`)
}

process.exit(1)
