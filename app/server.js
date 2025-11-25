const express = require('express');
const bodyParser = require('body-parser');
const redis = require('redis');
// 确保 config.json 文件在同一目录下
const config = require('./config.json'); 
const app = express();
const port = 3000;

// ----------------------------------------------------
// ⚠️ Redis 配置与连接
// ----------------------------------------------------
const redisClient = redis.createClient();

redisClient.on('error', (err) => {
    console.error('Redis Client Error:', err);
});

redisClient.connect().then(() => {
    console.log('✅ Connected to Redis successfully');
}).catch(err => {
    console.error('❌ Failed to connect to Redis:', err.message);
    process.exit(1); 
});

// 配置常量
const LATEST_STATUS_KEY = 'latest_device_status';
const TIMEOUT_MS = config.timeout_ms;

// ----------------------------------------------------
// ✅ 中间件设置
// ----------------------------------------------------
app.use(bodyParser.json());

// ----------------------------------------------------
// 📌 接口 1: POST /api/status (存储)
// ----------------------------------------------------
app.post('/api/status', async (req, res) => {
    const newStatus = req.body;
    if (Object.keys(newStatus).length === 0) {
        return res.status(400).json({ error: "Request body cannot be empty" });
    }
    newStatus.receivedAt = new Date().toISOString();
    try {
        await redisClient.set(LATEST_STATUS_KEY, JSON.stringify(newStatus));
        console.log(`[POST] New status updated at: ${newStatus.receivedAt}`);
        res.status(200).json({
            message: "Status received and stored successfully",
            data: newStatus
        });
    } catch (error) {
        console.error("Error storing data:", error);
        res.status(500).json({ error: "Internal Server Error" });
    }
});


/**
 * 接口 2: GET /api/status (读取、过滤与心跳检查)
 */
app.get('/api/status', async (req, res) => {
    try {
        const statusString = await redisClient.get(LATEST_STATUS_KEY);
        if (!statusString) {
            return res.status(404).json({ error: "No status data found." });
        }
        
        let latestStatus = JSON.parse(statusString);
        let finalStatus = { devices: {} };
        let globalConnectionStatus = "online";
        
        // --- 1. 设备可见性过滤与心跳检查 ---
        for (const [deviceName, deviceData] of Object.entries(latestStatus.devices || {})) {
            
            // 检查配置文件中是否允许显示该设备 (e.g., config.device_visibility["Phone"])
            if (config.device_visibility[deviceName] === true) {
                
                let deviceConnectionStatus = "online";
                
                // 只有当配置文件中 heartbeat_check 对应项为 true 时，才进行超时检查
                if (config.heartbeat_check[`${deviceName}_enabled`] === true) {
                    const lastUpdateTime = new Date(latestStatus.receivedAt).getTime();
                    const timeDifference = Date.now() - lastUpdateTime;
                    
                    if (timeDifference >= TIMEOUT_MS) {
                        deviceConnectionStatus = "disconnect";
                        globalConnectionStatus = "partial_disconnect"; 
                    }
                }
                
                // 构建最终返回的设备对象，并将连接状态合并到设备数据中
                finalStatus.devices[deviceName] = {
                    ...deviceData,
                    connectionStatus: deviceConnectionStatus 
                };
            }
        }

        // --- 2. 返回结果 ---
        res.status(200).json({
            globalConnectionStatus: globalConnectionStatus,
            receivedAt: latestStatus.receivedAt,
            ...finalStatus
        });

    } catch (error) {
        console.error("Error retrieving or parsing data:", error);
        res.status(500).json({ error: "Internal Server Error" });
    }
});


// ----------------------------------------------------
// 启动服务器
// ----------------------------------------------------
app.listen(port, () => {
    console.log(`🚀 Server running at http://localhost:${port}`);
    console.log(`Timeout for enabled devices is: ${TIMEOUT_MS / 1000} seconds.`);
});