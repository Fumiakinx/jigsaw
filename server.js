// Jigsawローカル開発テストサーバー (Node.js標準機能のみで動作する軽量スタティックサーバー)
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 8080;
const PUBLIC_DIR = __dirname;

// MIMEタイプの定義 (Unity WebGLおよびスタティックアセット対応)
const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.data': 'application/octet-stream',
    '.txt': 'text/plain; charset=utf-8'
};

const server = http.createServer((req, res) => {
    // CORSのプリフライトリクエスト (OPTIONS) に対処
    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type',
            'Access-Control-Max-Age': '86400'
        });
        res.end();
        return;
    }

    // URLデコード (日本語のパス名対策)
    let reqUrl = decodeURIComponent(req.url);
    // クエリ文字列の除去 (例: ?imageId=xxx)
    const qIndex = reqUrl.indexOf('?');
    if (qIndex !== -1) {
        reqUrl = reqUrl.substring(0, qIndex);
    }

    // デフォルトファイルのマッピング
    if (reqUrl === '/') {
        reqUrl = '/index.html';
    }

    const filePath = path.join(PUBLIC_DIR, reqUrl);

    // セキュリティ対策: ディレクトリトラバーサル防止の簡易チェック
    if (!filePath.startsWith(PUBLIC_DIR)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('403 Forbidden: アクセスが制限されています。');
        return;
    }

    fs.stat(filePath, (err, stats) => {
        if (err || !stats.isFile()) {
            console.log(`\x1b[31m[404] Not Found: ${reqUrl}\x1b[0m`);
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end('404 Not Found: 指定されたファイルが見つかりません。');
            return;
        }

        const ext = path.extname(filePath).toLowerCase();
        const contentType = MIME_TYPES[ext] || 'application/octet-stream';

        // レスポンスヘッダーの設定 (CORS有効化、WebGLアセットの適切な配信、キャッシュ無効化)
        const headers = {
            'Content-Type': contentType,
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type',
            'Cache-Control': 'no-store, no-cache, must-revalidate, private'
        };

        res.writeHead(200, headers);

        // ストリームによるファイル読み込みと配信 (大容量データ・Wasmに対応)
        const stream = fs.createReadStream(filePath);
        stream.on('error', (streamErr) => {
            console.error(`[ERROR] ストリーム配信エラー: ${streamErr.message}`);
            if (!res.headersSent) {
                res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('500 Internal Server Error');
            }
        });
        stream.pipe(res);
        console.log(`\x1b[32m[200] ${contentType} : ${reqUrl}\x1b[0m`);
    });
});

server.listen(PORT, () => {
    console.log('\n\x1b[36m==================================================\x1b[0m');
    console.log(`\x1b[36m🧩 JIGSAW ローカルテストサーバー起動完了 🧩\x1b[0m`);
    console.log(`\x1b[36m==================================================\x1b[0m`);
    console.log(`稼働ポート: \x1b[33m${PORT}\x1b[0m`);
    console.log(`テスト環境URL: \x1b[32mhttp://localhost:${PORT}/じぐそう/GoogleSiteEmbed.html\x1b[0m`);
    console.log(`サーバーを終了するには \x1b[33mCtrl + C\x1b[0m を押してください。\n`);
});
