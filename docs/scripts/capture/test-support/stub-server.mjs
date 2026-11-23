// A throwaway HTTP server for the capture tool's tests: it records every request it receives and
// answers through a handler the test supplies, so the client, the runner and the CLI are exercised
// against a real socket without a running CMS.
import { createServer } from 'node:http'

// handler(request) receives { method, path, headers, body } and returns
// { status, headers?, body? }; a missing body is an empty response. { hang: true } never answers and
// { destroy: true } closes the connection without an answer.
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
      }
      requests.push(received)
      const answer = handler(received) ?? { status: 404 }
      if (answer.hang) return
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
