interface ReconnectingWebSocketOptions {
  url: string
  binaryType?: BinaryType
  onMessage: (event: MessageEvent) => void
  onOpen?: () => void
  onClose?: () => void
}

export function connectReconnectingWebSocket(options: ReconnectingWebSocketOptions): () => void {
  let socket: WebSocket | null = null
  let reconnectTimer: number | null = null
  let attempts = 0
  let disposed = false

  const connect = () => {
    socket = new WebSocket(options.url)
    if (options.binaryType) socket.binaryType = options.binaryType
    socket.onopen = () => {
      attempts = 0
      options.onOpen?.()
    }
    socket.onmessage = options.onMessage
    socket.onerror = () => socket?.close()
    socket.onclose = () => {
      options.onClose?.()
      if (disposed) return
      const delay = Math.min(500 * 2 ** attempts, 5000)
      attempts += 1
      reconnectTimer = window.setTimeout(connect, delay)
    }
  }

  connect()
  return () => {
    disposed = true
    if (reconnectTimer !== null) window.clearTimeout(reconnectTimer)
    socket?.close()
  }
}
