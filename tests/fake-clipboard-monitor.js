// 模拟 C# sidecar 行为：输出预定义 JSON 消息后等待 stdin 关闭
const messages = [
    { type: 'text', payload: 'hello from fake' },
    { type: 'files', payload: ['C:\\file1.txt', 'C:\\file2.txt'] },
    { type: 'image_path', payload: 'C:\\temp\\fake.png' },
    { type: 'error', payload: 'fake error' }
];

messages.forEach((msg, i) => {
    setTimeout(() => {
        console.log(JSON.stringify(msg));
    }, (i + 1) * 30);
});

process.stdin.on('end', () => process.exit(0));
process.stdin.resume();
