#ifndef UIATHREAD_H
#define UIATHREAD_H

#include <QObject>
#include <QThread>
#include <QDebug>
#include <QMutex>
#include <QEventLoop>
#include <QTimer>

#include <Windows.h>
#include <psapi.h>
#include <UIAutomation.h>

#include <string>

class UIAThread : public QThread {
    Q_OBJECT
public:
    explicit UIAThread(const bool &selected, QObject* parent = nullptr)
        : QThread(parent), running(false), selectedPickState(selected), hasUpdateText(false) {
        moveToThread(this);
    }

    ~UIAThread() {
        if (pAutomation) {
            pAutomation->Release();
            pAutomation = nullptr;
        }
    }
    void run() override {
        // 初始化 IUIAutomation 实例
        HRESULT hr = CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_IUIAutomation, (void**)&pAutomation);
        if (FAILED(hr) || !pAutomation) {
            qWarning() << "Failed to initialize IUIAutomation";
            return;
        }

        // 创建事件循环和定时器
        QEventLoop eventLoop;
        QTimer timer;
        const int interval = 500; // 每500ms检查一次
        timer.setInterval(interval);

        // 连接定时器的 timeout 信号到处理逻辑
        connect(&timer, &QTimer::timeout, this, [&]() {
            if (!running) {
                eventLoop.quit(); // 停止事件循环
                return;
            }

            mutex.lock();
            if (paused) {
                mutex.unlock();
                return; // 暂停时不执行逻辑
            }
            mutex.unlock();

            // 检查鼠标位置
            POINT currentMousePos;
            if (GetCursorPos(&currentMousePos)) {
                // 如果鼠标位置未变，累积静止时间
                if (currentMousePos.x == lastMousePos.x && currentMousePos.y == lastMousePos.y) {
                    mouseStillTime += interval; // 每次增加500ms
                } else {
                    // 鼠标移动，重置静止时间
                    mouseStillTime = 0;
                    lastMousePos = currentMousePos;
                    hasUpdateText = false;
                }

                // 如果鼠标静止1秒，获取当前鼠标所在位置的前台进程名称
                if (mouseStillTime >= 1000) {
                    QString strProcessName;
                    if (!getWindowInfo(currentMousePos, strProcessName)) {
                        return;
                    }

                    QString capText;
                    uiaGet(strProcessName, currentMousePos, capText);
                    if (!hasUpdateText && !capText.isEmpty()) {
                        emit textShow(capText);
                        qDebug() << "UIAThread: capText = " << capText;
                        hasUpdateText = true;
                    }
                }
            }
        });

        // 启动定时器
        timer.start();

        // 进入事件循环
        eventLoop.exec();

        // 清理
        timer.stop();
        if (pAutomation) {
            pAutomation->Release();
            pAutomation = nullptr;
        }
    }

    void startMonitoring() {
        if (!isRunning()) {
            running = true;
            paused = false;
            start(); // 启动线程
        } else {
            resumeMonitoring(); // 如果线程已在运行，仅恢复
        }
    }

    void pauseMonitoring() {
        mutex.lock();
        paused = true;
        mutex.unlock();
    }

    void resumeMonitoring() {
        mutex.lock();
        paused = false;
        mutex.unlock();
    }

    void stopMonitoring() {
        mutex.lock();
        running = false;
        paused = false;
        mutex.unlock();
        quit();
        wait();
    }

signals:
    void textShow(const QString& text);

public slots:
    void updateSelectedPick(const bool &selected){
        selectedPickState = selected;
        qDebug() << QString("UIAThread::updateSelectedPick(%1)").arg(selected);
    }

private:
    void uiaGet(const QString &processName, const POINT &cursorPos, QString &text){
        if (!pAutomation) {
            qDebug() << "UIA Automation not initialized";
            return;
        }

        // 获取鼠标位置的元素
        IUIAutomationElement* pElement = nullptr;
        HRESULT hr = pAutomation->ElementFromPoint(cursorPos, &pElement);
        if (FAILED(hr) || !pElement) {
            qDebug() << "Failed to get element at cursor, HRESULT:" << QString::number(hr, 16);
            return;
        }

        // 检查控件类型
        CONTROLTYPEID controlType;
        pElement->get_CurrentControlType(&controlType);
#ifdef QT_DEBUG
        qDebug() << "ControlType:" << controlType;
#endif

        qDebug() << QString("%1, %2, %3").arg(selectedPickState).arg(processName).arg(controlType);
        if (controlType == UIA_EditControlTypeId ||
            controlType == UIA_DocumentControlTypeId ||
            controlType == UIA_PaneControlTypeId ||
            (selectedPickState && (processName == "Weixin.exe") && (controlType == UIA_ListItemControlTypeId))) {
            return;
        }

        // 尝试获取 Name 属性
        BSTR name;
        if (SUCCEEDED(pElement->get_CurrentName(&name)) && name) {
            text = QString::fromWCharArray(name);
            SysFreeString(name);
        }

        pElement->Release();
    }

    bool getWindowInfo(POINT cursorPos, QString& strProcessName) {
        HWND hwnd = WindowFromPoint(cursorPos);
        if (!hwnd) return false;

        // 获取窗口关联的进程 ID
        DWORD processId = 0;
        GetWindowThreadProcessId(hwnd, &processId);
        if (processId == 0) return false;

        // 打开进程以查询信息
        HANDLE hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, FALSE, processId);
        if (!hProcess) {
            DWORD error = GetLastError();
            qDebug() << "OpenProcess failed, error code:" << error;
            return false;
        }

        // 获取进程名
        wchar_t processPath[MAX_PATH] = L"";
        DWORD pathLength = MAX_PATH;
        BOOL result = QueryFullProcessImageNameW(hProcess, 0, processPath, &pathLength);
        CloseHandle(hProcess);

        if (result == 0) {
            DWORD error = GetLastError();
            qDebug() << QString("Failed to get process name. process id: %1, error code: %2, error msg: %3").arg(processId).arg(error).arg(GetErrorMessage(error));
            return false;
        }
        // 从路径中提取文件名
        strProcessName = QString::fromWCharArray(processPath).section('\\', -1);
        return true;
    }

    // 函数：将 Windows 错误码转换为可读的 wstring 字符串
    std::wstring GetErrorMessage(DWORD errorCode) {
        // 如果错误码为 0，表示没有错误
        if (errorCode == 0) {
            return L"No error.";
        }

        LPWSTR messageBuffer = nullptr;
        // 调用 FormatMessageW 函数
        // 参数 dwFlags 告诉函数：
        // 1. FORMAT_MESSAGE_ALLOCATE_BUFFER: 函数自己分配缓冲区来存储消息文本
        // 2. FORMAT_MESSAGE_FROM_SYSTEM: 从操作系统消息表中查找消息
        // 3. FORMAT_MESSAGE_IGNORE_INSERTS: 忽略消息文本中的插入序列
        size_t size = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            NULL,
            errorCode,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), // 使用用户默认语言
            (LPWSTR)&messageBuffer,
            0,
            NULL
            );

        if (size == 0) {
            // 如果 FormatMessage 失败，返回一个通用错误信息
            return L"Unable to format error message for error code " + std::to_wstring(errorCode);
        }

        // 将 C 风格的字符串拷贝到 std::wstring 中
        std::wstring message(messageBuffer, size);

        // !! 关键步骤：释放由 FormatMessageW 分配的缓冲区
        LocalFree(messageBuffer);

        return message;
    }

    bool running;
    bool paused;
    QMutex mutex;
    IUIAutomation* pAutomation = nullptr;

    POINT lastMousePos; // 上次鼠标位置
    int mouseStillTime; // 鼠标静止时间（毫秒）

    bool selectedPickState;     // 划选取词使能
    bool hasUpdateText;         // 已提交选词标识
};

#endif // UIATHREAD_H
