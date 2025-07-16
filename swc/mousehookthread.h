#ifndef MOUSEHOOKTHREAD_H
#define MOUSEHOOKTHREAD_H

#include <QObject>
#include <QThread>
#include <QDebug>
#include <QMutex>
#include <QApplication>
#include <QClipboard>
#include <Windows.h>
#include <QTimer>
#include <string>
#include <psapi.h>
#include <UIAutomation.h>
#include <richedit.h>

class MouseHookThread : public QThread {
    Q_OBJECT
public:
    explicit MouseHookThread(QObject* parent = nullptr)
        : QThread(parent), running(false), hasUpdateText(false), mouseDown(false) {
        hHook = nullptr;
        QMutexLocker locker(&instanceMutex);
        if (instance != nullptr) {
            qWarning() << "MouseHookThread already exists! Overwriting instance.";
        }
        instance = this;
        moveToThread(this);
    }

    ~MouseHookThread() {
        stopMonitoring();
        QMutexLocker locker(&instanceMutex);
        instance = nullptr;
        if (hHook) {
            UnhookWindowsHookEx(hHook);
            hHook = nullptr;
        }
    }

    void run() override {
        HRESULT hr = CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_IUIAutomation, (void**)&pAutomation);
        if (FAILED(hr) || !pAutomation) {
            qWarning() << "Failed to initialize IUIAutomation";
        }

        // 连接剪贴板信号
        connect(QApplication::clipboard(), &QClipboard::dataChanged, this, &MouseHookThread::onClipboardChanged);

        // 安装全局鼠标钩子
        hHook = SetWindowsHookEx(WH_MOUSE_LL, MouseProc, nullptr, 0);
        if (!hHook) {
            qDebug() << "Failed to install mouse hook, error:" << GetLastError();
            return;
        }

        eventLoop = new QEventLoop(this);
        connect(this, &QThread::finished, eventLoop, &QEventLoop::quit);
        eventLoop->exec();

        // 卸载钩子
        if (hHook) {
            UnhookWindowsHookEx(hHook);
            hHook = nullptr;
        }
    }

    void startMonitoring() {
        if (!isRunning()) {
            running = true;
            start();
        } else if (eventLoop) {
            eventLoop->processEvents(); // 确保事件队列清空
            eventLoop->blockSignals(false); // 恢复事件处理
        }
    }

    void pauseMonitoring() {
        running = false;
        if (eventLoop) {
            eventLoop->blockSignals(true); // 暂停事件处理
        }

    }

    void resumeMonitoring() {
        running = true;
        if (eventLoop) {
            eventLoop->processEvents(); // 处理积压事件
            eventLoop->blockSignals(false); // 恢复事件处理
        }
    }

    void stopMonitoring() {
        running = false;
        if (eventLoop) {
            eventLoop->exit(); // 退出事件循环
        }
        if (hHook) {
            UnhookWindowsHookEx(hHook);
            hHook = nullptr;
        }
        if (isRunning()) {
            wait(); // 等待线程退出
        }
    }


signals:
    void textShow(const QString& text);

private slots:
    void onClipboardChanged() {
        if (!running || !isDragging || hasUpdateText){
            qDebug() << QString("running = %1, isDragging = %2, hasUpdateText = %3").arg(running).arg(isDragging).arg(hasUpdateText);
            return;
        }

        // 获取剪贴板内容
        QClipboard* clipboard = QApplication::clipboard();
        QString text = clipboard->text().trimmed();

        // 验证文本有效性
        if (text.isEmpty() || text.length() > 1024) {
            return;
        }

        emit textShow(text);
        hasUpdateText = true;
        qDebug() << "MouseHookThread: capText =" << text;
        isDragging = false; // 重置拖动状态
    }

private:
    static LRESULT CALLBACK MouseProc(int nCode, WPARAM wParam, LPARAM lParam) {
        QMutexLocker locker(&instanceMutex);
        if (nCode >= 0 && instance) {
            instance->handleMouseEvent(nCode, wParam, lParam);
        }
        return CallNextHookEx(nullptr, nCode, wParam, lParam);
    }

    void handleMouseEvent(int nCode, WPARAM wParam, LPARAM lParam) {
        Q_UNUSED(nCode);
        if (!running) return;
        MSLLHOOKSTRUCT* mouseStruct = reinterpret_cast<MSLLHOOKSTRUCT*>(lParam);
        POINT cursorPos = mouseStruct->pt;
        switch (wParam) {
        case WM_LBUTTONDOWN:
            mouseDown = true;
            isDragging = false;
            startPoint = cursorPos;
            lastDraggingPoint = cursorPos;
            hasUpdateText = false;
            break;
        case WM_MOUSEMOVE:
            if (mouseDown && !isDragging) {
                // 计算与起始点的距离
                int dx = abs(cursorPos.x - startPoint.x);
                int dy = abs(cursorPos.y - startPoint.y);
                if (dx > DRAG_THRESHOLD || dy > DRAG_THRESHOLD) {
                    isDragging = true; // 标记为有效拖动
                    lastDraggingPoint = cursorPos;
                }
            } else if (mouseDown && isDragging) {
                lastDraggingPoint = cursorPos; // 更新拖动位置
            }
            break;
        case WM_LBUTTONUP:
            if (mouseDown) {
                mouseDown = false;
                if (isDragging && !hasUpdateText) {
                    lastWindow = WindowFromPoint(cursorPos);
                    if (lastWindow) {
                        // 增强焦点管理
                        DWORD currentThreadId = GetCurrentThreadId();
                        DWORD targetThreadId = GetWindowThreadProcessId(lastWindow, nullptr);
                        if (currentThreadId != targetThreadId) {
                            AttachThreadInput(currentThreadId, targetThreadId, TRUE);
                            SetForegroundWindow(lastWindow);
                            SetFocus(lastWindow);
                            AttachThreadInput(currentThreadId, targetThreadId, FALSE);
                        } else {
                            SetForegroundWindow(lastWindow);
                            SetFocus(lastWindow);
                        }
                        QString processName;
                        getWindowInfo(cursorPos, processName);
                        if(processName == "notepad.exe" ||
                            processName == "WINWORD.EXE"){
                            QString text = getUIASelectedText(cursorPos);
                            if(!text.isEmpty()){
                                emit textShow(text);
                                hasUpdateText = true;
                                qDebug() << "MouseHookThread: capText =" << text;
                                isDragging = false; // 重置拖动状态
                            }
                            qDebug() << "getUIASelectedText(cursorPos) -> " << text;
                        }else{
                            if(!processName.isEmpty()){
                                // 尝试触发 Ctrl+C
                                triggerCopy();
                            }
                        }
                    }
                }
            }
            break;
        }
    }

    void triggerCopy() {
        // 模拟 Ctrl+C
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event('C', 0, 0, 0);
        keybd_event('C', 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }

    bool getWindowInfo(POINT cursorPos, QString& strProcessName) {
        HWND hwnd = WindowFromPoint(cursorPos);
        if (!hwnd) return false;

        DWORD processId = 0;
        GetWindowThreadProcessId(hwnd, &processId);
        if (processId == 0) return false;

        HANDLE hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, FALSE, processId);
        if (!hProcess) {
            qDebug() << "OpenProcess failed, error code:" << GetLastError();
            return false;
        }

        wchar_t processPath[MAX_PATH] = L"";
        DWORD pathLength = MAX_PATH;
        BOOL result = QueryFullProcessImageNameW(hProcess, 0, processPath, &pathLength);
        CloseHandle(hProcess);

        if (result == 0) {
            qDebug() << QString("Failed to get process name. process id: %1, error code: %2").arg(processId).arg(GetLastError());
            return false;
        }

        strProcessName = QString::fromWCharArray(processPath).section('\\', -1);
        return true;
    }

    QString getUIASelectedText(const POINT& cursorPos) {
        if (!pAutomation) {
            qDebug() << "UIA Automation not initialized";
            return QString();
        }

        qDebug() << "getUIASelectedText: Starting";

        IUIAutomationElement* pElement = nullptr;
        HRESULT hr = pAutomation->ElementFromPoint(cursorPos, &pElement);
        if (FAILED(hr) || !pElement) {
            qDebug() << "Failed to get element at cursor, HRESULT: 0x" << QString::number(hr, 16);
            return QString();
        }

        // 检查控件类型
        CONTROLTYPEID controlType;
        hr = pElement->get_CurrentControlType(&controlType);
        if (FAILED(hr)) {
            qDebug() << "Failed to get control type, HRESULT: 0x" << QString::number(hr, 16);
            pElement->Release();
            return QString();
        }
        if (controlType != UIA_EditControlTypeId) {
            qDebug() << "Element is not Edit control, ControlType: " << controlType;
            pElement->Release();
            return QString();
        }
        qDebug() << "Element is Edit control (UIA_EditControlTypeId)";

        // 尝试获取 TextPattern
        IUIAutomationTextPattern* pTextPattern = nullptr;
        hr = pElement->GetCurrentPattern(UIA_TextPatternId, (IUnknown**)&pTextPattern);
        if (FAILED(hr) || !pTextPattern) {
            qDebug() << "Failed to get TextPattern, HRESULT: 0x" << QString::number(hr, 16);
            // 备用方案：尝试通过 WM_GETTEXT 获取文本
            QString fallbackText = getFallbackText(pElement);
            pElement->Release();
            return fallbackText;
        }
        qDebug() << "TextPattern acquired";

        // 获取选中文本
        IUIAutomationTextRangeArray* pSelection = nullptr;
        hr = pTextPattern->GetSelection(&pSelection);
        if (FAILED(hr) || !pSelection) {
            qDebug() << "Failed to get selection, HRESULT: 0x" << QString::number(hr, 16);
            pTextPattern->Release();
            pElement->Release();
            return QString();
        }
        qDebug() << "Selection acquired";

        QString selectedText;
        int length = 0;
        pSelection->get_Length(&length);
        qDebug() << "Selection length: " << length;
        if (length > 0) {
            IUIAutomationTextRange* pRange = nullptr;
            hr = pSelection->GetElement(0, &pRange);
            if (SUCCEEDED(hr) && pRange) {
                BSTR text;
                if (SUCCEEDED(pRange->GetText(-1, &text)) && text) {
                    selectedText = QString::fromWCharArray(text);
                    SysFreeString(text);
                    qDebug() << "Selected text: " << selectedText;
                } else {
                    qDebug() << "Failed to get text from range, HRESULT: 0x" << QString::number(hr, 16);
                }
                pRange->Release();
            } else {
                qDebug() << "Failed to get selection range, HRESULT: 0x" << QString::number(hr, 16);
            }
        } else {
            qDebug() << "No text selected (selection length is 0)";
        }

        pSelection->Release();
        pTextPattern->Release();
        pElement->Release();
        return selectedText;
    }

    QString getFallbackText(IUIAutomationElement* pElement) {
        qDebug() << "Attempting fallback text retrieval via EM_GETSELTEXT";

        // 获取窗口句柄
        HWND hwnd = nullptr;
        HRESULT hr = pElement->get_CurrentNativeWindowHandle(reinterpret_cast<UIA_HWND*>(&hwnd));
        if (FAILED(hr) || !hwnd) {
            qDebug() << "Failed to get window handle, HRESULT: 0x" << QString::number(hr, 16);
            return QString();
        }
        qDebug() << "Window handle acquired: " << hwnd;

        // 确保窗口有焦点
        DWORD currentThreadId = GetCurrentThreadId();
        DWORD targetThreadId = GetWindowThreadProcessId(hwnd, nullptr);
        if (currentThreadId != targetThreadId) {
            AttachThreadInput(currentThreadId, targetThreadId, TRUE);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            Sleep(50); // 添加延迟确保焦点生效
            AttachThreadInput(currentThreadId, targetThreadId, FALSE);
        } else {
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            Sleep(50);
        }

        // 获取选区范围
        DWORD start = 0, end = 0;
        LRESULT result = SendMessageW(hwnd, EM_GETSEL, (WPARAM)&start, (LPARAM)&end);
        qDebug() << "EM_GETSEL result: " << result << ", start: " << start << ", end: " << end;
        if (start != end) { // 有选中文本
            // 尝试使用 EM_GETSELTEXT 获取选中文本
            int selLength = end - start + 1; // +1 for null terminator
            wchar_t* selBuffer = new wchar_t[selLength];
            selBuffer[0] = L'\0'; // Initialize to empty
            result = SendMessageW(hwnd, EM_GETSELTEXT, 0, (LPARAM)selBuffer);
            if (result > 0) {
                QString text = QString::fromWCharArray(selBuffer);
                delete[] selBuffer;
                qDebug() << "EM_GETSELTEXT retrieved text: " << text;
                return text;
            } else {
                qDebug() << "EM_GETSELTEXT failed, result: " << result;
                delete[] selBuffer;

                // 替代方案：使用 WM_GETTEXT 截取选区
                qDebug() << "Attempting WM_GETTEXT with selection range";
                wchar_t buffer[4096] = {0};
                result = SendMessageW(hwnd, WM_GETTEXT, sizeof(buffer) / sizeof(wchar_t), (LPARAM)buffer);
                if (result > 0) {
                    QString fullText = QString::fromWCharArray(buffer);
                    if (start < fullText.length() && end <= fullText.length()) {
                        QString selectedText = fullText.mid(start, end - start);
                        qDebug() << "Selected text via WM_GETTEXT: " << selectedText;
                        return selectedText;
                    } else {
                        qDebug() << "Invalid selection range for WM_GETTEXT, fullText length: " << fullText.length();
                    }
                } else {
                    qDebug() << "WM_GETTEXT failed, result: " << result;
                }
            }
        } else {
            qDebug() << "No text selected (start == end)";
        }

        return QString();
    }

    std::wstring GetErrorMessage(DWORD errorCode) {
        if (errorCode == 0) {
            return L"No error.";
        }

        LPWSTR messageBuffer = nullptr;
        size_t size = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            NULL,
            errorCode,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&messageBuffer,
            0,
            NULL
            );

        if (size == 0) {
            return L"Unable to format error message for error code " + std::to_wstring(errorCode);
        }

        std::wstring message(messageBuffer, size);
        LocalFree(messageBuffer);
        return message;
    }

    bool running;
    bool hasUpdateText;
    bool mouseDown;
    bool isDragging;
    POINT startPoint;
    POINT lastDraggingPoint;
    HHOOK hHook;
    QEventLoop* eventLoop = nullptr;
    HWND lastWindow;            // 保存最后划词的窗口句柄
    IUIAutomation* pAutomation = nullptr;
private:
    static constexpr int DRAG_THRESHOLD = 5;
    static MouseHookThread* instance;
    static QMutex instanceMutex;
};

#endif // MOUSEHOOKTHREAD_H
