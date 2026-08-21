import { openDB, type DBSchema } from 'idb'

export interface CaptureContext {
  captureId: string
  takeId: string
  sessionId: string
  sentenceId: string
  mimeType: string
  scheduledAtUnixMs: number
  startedAtUnixMs: number
  firstChunkAtUnixMs: number
  stoppedAtUnixMs: number
}

interface CaptureChunk {
  id: string
  captureId: string
  sequence: number
  blob: Blob
}

interface CaptureDatabase extends DBSchema {
  captures: {
    key: string
    value: CaptureContext
  }
  chunks: {
    key: string
    value: CaptureChunk
    indexes: { 'by-capture': string }
  }
}

const database = openDB<CaptureDatabase>('signvr-camera-captures-v2', 1, {
  upgrade(db) {
    db.createObjectStore('captures', { keyPath: 'captureId' })
    const chunks = db.createObjectStore('chunks', { keyPath: 'id' })
    chunks.createIndex('by-capture', 'captureId')
  },
})

export function createCaptureId(sessionId: string, sentenceId: string, takeId: string): string {
  return `${sessionId}\u001f${sentenceId}\u001f${takeId}`
}

export async function saveCaptureContext(context: CaptureContext): Promise<void> {
  const db = await database
  await db.put('captures', context)
}

export async function appendCaptureChunk(
  captureId: string,
  sequence: number,
  blob: Blob,
): Promise<void> {
  const db = await database
  await db.put('chunks', {
    id: `${captureId}\u001f${String(sequence).padStart(8, '0')}`,
    captureId,
    sequence,
    blob,
  })
}

export async function listPendingCaptures(): Promise<CaptureContext[]> {
  const db = await database
  return db.getAll('captures')
}

export async function loadCaptureBlob(context: CaptureContext): Promise<Blob> {
  const db = await database
  const chunks = await db.getAllFromIndex('chunks', 'by-capture', context.captureId)
  chunks.sort((left, right) => left.sequence - right.sequence)
  return new Blob(chunks.map((chunk) => chunk.blob), { type: context.mimeType })
}

export async function removeCapture(captureId: string): Promise<void> {
  const db = await database
  const transaction = db.transaction(['captures', 'chunks'], 'readwrite')
  const chunks = await transaction.objectStore('chunks').index('by-capture').getAllKeys(captureId)
  await Promise.all([
    transaction.objectStore('captures').delete(captureId),
    ...chunks.map((key) => transaction.objectStore('chunks').delete(key)),
  ])
  await transaction.done
}
