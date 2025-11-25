import subprocess
import re
import json
import time
import requests
import sys

# ----------------------------------------------------
# 📌 配置信息
# ----------------------------------------------------
SERVER_URL = "http://localhost:3000/api/status" 
DEVICE_ADDRESS = "192.168.124.249:38887" 
CHECK_INTERVAL_SECONDS = 5 
AAPT_PATH = "/data/local/tmp/aapt-arm-pie" # ⚠️ 假设 aapt-arm-pie 的路径

# 全局状态跟踪
LAST_REPORTED_LABEL = "initial" 
MAX_ERRORS = 3 

# ----------------------------------------------------
# ⚠️ 本地查找表 (App Label Map) - 仅作为高速缓存
# ----------------------------------------------------
APP_LABEL_MAP = {
    "com.tencent.mm": "微信",
    "com.tencent.mobileqq": "QQ",
    "com.ss.android.ugc.aweme": "抖音",
    "tv.danmaku.bili": "哔哩哔哩", # B站已存在，但可以删除，让AAPT去发现
    "com.android.launcher3": "桌面启动器",
    "com.miui.home": "小米桌面",
    # [请删除或只保留您常用应用的标签，让AAPT去发现新的应用]
}

# ----------------------------------------------------
# ✅ 核心函数
# ----------------------------------------------------

def execute_adb_command(command):
    """执行 ADB Shell 命令并返回输出。（保持不变）"""
    try:
        result = subprocess.run(
            ['adb', 'shell', command],
            capture_output=True,
            text=True,
            check=True,
            encoding='utf-8',
            timeout=10
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError:
        return None
    except Exception:
        return None

def connect_adb(address):
    # ... (连接函数保持不变) ...
    print(f"尝试连接 ADB 设备: {address}...")
    try:
        result = subprocess.run(
            ['adb', 'connect', address],
            capture_output=True,
            text=True,
            timeout=10
        )
        output = result.stdout.strip()
        
        if "connected to" in output or "already connected to" in output:
            print(f"   ✅ ADB 连接成功: {output}")
            return True
        else:
            print(f"   ❌ ADB 连接失败。输出: {output}")
            return False
            
    except subprocess.TimeoutExpired:
        print("   ❌ ADB 连接超时。")
        return False
    except Exception as e:
        print(f"   ❌ 连接过程中发生错误: {e}")
        return False

def get_foreground_package():
    """采用双重检测机制获取当前前台应用的包名。（保持不变）"""
    
    # --- 1. 主方案 (Fast & Confirmed Working) ---
    adb_output_fast = execute_adb_command(
        "am stack list | grep -E 'topActivity='" 
    )

    if adb_output_fast is not None:
        match = re.search(r'topActivity=ComponentInfo{([\w\.]+)/', adb_output_fast)
        if match:
            return match.group(1)

    # --- 2. 备用方案 (Slower but Robust dumpsys window) ---
    print("   ⚠️ 切换至备用方案: dumpsys window windows (较慢)")
    adb_output_slow = execute_adb_command(
        "dumpsys window windows | grep -E 'mFocusedApp='"
    )
    
    if adb_output_slow is not None:
        match = re.search(r'mFocusedApp=ActivityRecord\{[^\s]+ u0 ([\w\.]+)/', adb_output_slow)
        if match:
            return match.group(1)

    # --- 3. 最终失败 ---
    return "System UI / Launcher"

# --- AAPT 逻辑 ---
def get_app_label_from_adb_aapt(package_name):
    """通过 ADB Shell 和 aapt-arm-pie 工具动态获取应用标签。"""
    print(f"   ⚙️ 尝试使用 ADB/AAPT 获取 '{package_name}' 的标签 (首次发现，较慢)...")

    # Step 1: Get APK Path
    path_output = execute_adb_command(f"pm path {package_name}")
    if not path_output or not path_output.startswith("package:"):
        print("   ❌ AAPT 失败: 无法获取 APK 路径。")
        return None
    
    # Extract path: package:/data/.../base.apk
    apk_path = path_output.replace("package:", "").strip()

    # Step 2: Use AAPT to dump badging and find label
    # 避免在shell中使用grep，将解析放在Python中
    aapt_command = f"{AAPT_PATH} d badging {apk_path}"
    aapt_output = execute_adb_command(aapt_command)
    
    if not aapt_output:
        print("   ❌ AAPT 失败: 命令执行返回空或错误。")
        return None

    # Step 3: Parse the label from the massive output (Search for: application: label='...')
    match = re.search(r"application: label='([^']+)'", aapt_output)
    
    if match:
        label = match.group(1)
        print(f"   ✅ AAPT 成功: 标签为 '{label}'。")
        return label
    else:
        print("   ❌ AAPT 失败: 无法从输出中解析标签。")
        return None
# --- AAPT 逻辑 END ---


def get_app_label(package_name):
    """三层冗余获取应用标签：本地查找 -> AAPT 动态获取 -> 包名。"""
    
    if package_name in ["ADB_ERROR", "System UI / Launcher"]:
        return package_name
    
    # 1. 本地查找 (最快)
    label = APP_LABEL_MAP.get(package_name, None)
    if label:
        return label.strip()
    
    # 2. AAPT 动态获取 (较慢)
    aapt_label = get_app_label_from_adb_aapt(package_name)
    if aapt_label:
        # ⚠️ 可选：如果AAPT成功，动态加入MAP中，加速下次查找
        # APP_LABEL_MAP[package_name] = aapt_label 
        return aapt_label.strip()

    # 3. 最终返回包名 (最低优先级)
    print(f"   ⚠️ 警告: 包名 '{package_name}' 既不在MAP中，AAPT也失败了，使用包名代替。")
    return package_name.strip()


def upload_status(package_label, is_disconnect=False):
    # ... (上传函数保持不变) ...
    global LAST_REPORTED_LABEL

    if is_disconnect:
        phone_status = "disconnect"
        software_name = "N/A"
    else:
        phone_status = "online"
        software_name = package_label 

    payload = {
        "devices": {
            "Phone": {
                "status": phone_status,
                "software": software_name 
            }
        }
    }

    try:
        response = requests.post(SERVER_URL, json=payload, timeout=10)
        
        if response.status_code == 200:
            print(f"   ✅ 状态上传成功！Status: {phone_status}, App: {software_name}")
            LAST_REPORTED_LABEL = software_name if not is_disconnect else "disconnect"
        else:
            print(f"   ⚠️ 上传失败。服务器返回状态码: {response.status_code}")

    except requests.exceptions.RequestException as e:
        print(f"   ❌ 请求失败 (网络/连接错误): {e}")


def main():
    print("--------------------------------------------------")
    print(f"🚀 启动手机应用监控程序...")
    print(f"上传服务: {SERVER_URL}")
    print(f"间隔时间: {CHECK_INTERVAL_SECONDS} 秒")
    print(f"AAPT路径: {AAPT_PATH}")
    print("--------------------------------------------------")

    if not connect_adb(DEVICE_ADDRESS):
        print("致命错误：无法建立 ADB 连接。程序退出。")
        sys.exit(1)

    global LAST_REPORTED_LABEL
    consecutive_error_count = 0
    
    while True:
        try:
            timestamp = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime())
            
            current_package = get_foreground_package()
            current_label = get_app_label(current_package) 
            
            print(f"[{timestamp}] DEBUG CHECK - Package: {current_package}, Label: {current_label}")
            
            if current_package == "ADB_ERROR":
                consecutive_error_count += 1
                
                if consecutive_error_count >= MAX_ERRORS:
                    if LAST_REPORTED_LABEL != "disconnect":
                        print(f"\n[{timestamp}] ⚠️ 连续错误，发送 DISCONNECT 信号...")
                        upload_status("N/A", is_disconnect=True)
                    
                    print("执行 adb disconnect 清理旧连接...")
                    subprocess.run(['adb', 'disconnect', DEVICE_ADDRESS], capture_output=False) 
                    
                    connect_adb(DEVICE_ADDRESS)

            else:
                consecutive_error_count = 0
                
                if current_label != LAST_REPORTED_LABEL and current_label not in ["System UI / Launcher", "initial"]:
                    print(f"\n[{timestamp}] 应用变更: {current_label}")
                    upload_status(current_label) 
                elif LAST_REPORTED_LABEL == "disconnect":
                    print(f"\n[{timestamp}] 连接恢复，发送当前应用状态: {current_label}")
                    upload_status(current_label) 
            
            time.sleep(CHECK_INTERVAL_SECONDS)

        except KeyboardInterrupt:
            print("\n👋 程序已停止。")
            break
        except Exception as e:
            print(f"发生未预期的错误: {e}")
            time.sleep(5)

if __name__ == "__main__":
    main()