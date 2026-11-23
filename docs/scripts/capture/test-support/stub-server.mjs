// A throwaway HTTP server for the capture tool's tests: it records every request it receives and
// answers through a handler the test supplies, so the client, the runner and the CLI are exercised
// against a real socket without a running CMS.
import { createServer } from 'node:http'

// handler(request) receives { method, path, headers, body, rawBody, connection } (connection tells one
// client socket from another) and returns
// { status, headers?, body? }; a missing body is an empty response. { hang: true } never answers and
// { destroy: true } closes the connection without an answer, and { cut: "text" } sends the headers
// of a longer body, then half of it, then drops the connection.
export async function startStub(handler) {
  const requests = []
  const server = createServer((req, res) => {
    const chunks = []
    req.on('data', (chunk) => chunks.push(chunk))
    req.on('end', () => {
      const received = {
        method: req.method,
        path: req.url,
        headers: req.headers,
        body: Buffer.concat(chunks).toString('utf8'),
        rawBody: Buffer.concat(chunks),
        connection: req.socket.remotePort,
      }
      requests.push(received)
      const answer = handler(received) ?? { status: 404 }
      if (answer.hang) return
      if (answer.cut) {
        res.writeHead(200, { 'Content-Length': String(answer.cut.length * 2) })
        res.write(answer.cut)
        setTimeout(() => req.socket.destroy(), 30)
        return
      }
      if (answer.destroy) {
        req.socket.destroy()
        return
      }
      res.writeHead(answer.status, answer.headers ?? {})
      res.end(answer.body ?? '')
    })
  })
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve))
  const { port } = server.address()
  return {
    baseUrl: `http://127.0.0.1:${port}`,
    requests,
    close: () =>
      new Promise((resolve) => {
        server.closeAllConnections()
        server.close(resolve)
      }),
  }
}
