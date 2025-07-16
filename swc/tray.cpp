#include "tray.h"

#include <QInputDialog>
#include <QMessageBox>
#include <QApplication>
#include <QDesktopServices>
#include <QUrl>
#include <QVBoxLayout>
#include <QActionGroup>
#include <QMouseEvent>
#include <QTime>

AboutDialog::AboutDialog(QWidget *parent) : QDialog(parent) {
    setWindowTitle("屏幕取词Demo");
    setWindowIcon(QIcon(":/res/bracket_11607099.png"));

    QVBoxLayout *layout = new QVBoxLayout(this);

    QLabel *label = new QLabel(this);
    label->setText("开发者: <b>louis</b><br><br><a href='https://www.iamlouis.online'>https://www.iamlouis.online</a>");
    label->setTextInteractionFlags(Qt::TextBrowserInteraction | Qt::LinksAccessibleByMouse);
    label->setOpenExternalLinks(true); // 自动打开外部链接

    layout->addWidget(label);
    layout->addStretch();

    setMaximumHeight(50);
}

Tray::Tray(QObject *parent)
    : QObject{parent}, bubble(nullptr), mouseCheckTimer(nullptr)
{
    // 初始化 COM
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

    trayIcon = new QSystemTrayIcon(this);
    trayIcon->setIcon(QIcon(":/res/bracket_11607099.png"));

    trayMenu = new QMenu();

    dockPickAction = new QWidgetAction(this);
    QWidget* wordPickWidget = new QWidget();
    QHBoxLayout* wordPickLayout = new QHBoxLayout(wordPickWidget);
    dockPickCheckBox = new QCheckBox("停靠取词");
    dockPickCheckBox->setChecked(true);
    wordPickLayout->addWidget(dockPickCheckBox);
    wordPickLayout->addSpacing(4);
    wordPickLayout->addStretch();
    wordPickWidget->setLayout(wordPickLayout);
    dockPickAction->setDefaultWidget(wordPickWidget);

    selectedPickAction = new QWidgetAction(this);
    QWidget* sentencePickWidget = new QWidget();
    QHBoxLayout* sentencePickLayout = new QHBoxLayout(sentencePickWidget);
    selectedPickCheckBox = new QCheckBox("划选取词");
    sentencePickLayout->addWidget(selectedPickCheckBox);
    sentencePickLayout->addSpacing(4);
    sentencePickLayout->addStretch();
    sentencePickWidget->setLayout(sentencePickLayout);
    selectedPickAction->setDefaultWidget(sentencePickWidget);

    screenshotAction = new QWidgetAction(this);
    QWidget* screenshotWidget = new QWidget();
    QHBoxLayout* screenshotLayout = new QHBoxLayout(screenshotWidget);
    screenshotCheckBox = new QCheckBox("截图取词");
    screenshotCheckBox->setDisabled(true);
    screenshotLayout->addWidget(screenshotCheckBox);
    screenshotLayout->addSpacing(4);
    screenshotLayout->addStretch();
    screenshotWidget->setLayout(screenshotLayout);
    screenshotAction->setDefaultWidget(screenshotWidget);

    QActionGroup* actionGroup = new QActionGroup(this);
    actionGroup->addAction(dockPickAction);
    actionGroup->addAction(selectedPickAction);
    actionGroup->addAction(screenshotAction);
    actionGroup->setExclusive(true);

    trayMenu->addAction(dockPickAction);
    trayMenu->addAction(selectedPickAction);
    trayMenu->addAction(screenshotAction);

    trayMenu->addSeparator();

    aboutAction = new QAction("关于", this);
    trayMenu->addAction(aboutAction);

    trayMenu->addSeparator();

    quitAction = new QAction("退出", this);
    trayMenu->addAction(quitAction);

    trayIcon->setContextMenu(trayMenu);

    connect(dockPickCheckBox, &QCheckBox::toggled, this, [this](bool checked) {
        if (checked) {
            uiaThread->resumeMonitoring();
        } else {
            uiaThread->pauseMonitoring();
        }
    });
    connect(selectedPickCheckBox, &QCheckBox::toggled, this, [this](bool checked) {
        if (checked) {
            hookThread->resumeMonitoring();
        } else {
            hookThread->pauseMonitoring();
        }
        emit updateSelectedPick(checked);
        qDebug() << QString("emit updateSelectedPick(%1)").arg(checked);
    });
    connect(screenshotCheckBox, &QCheckBox::toggled, this, [](bool checked) {
        if (checked) {
            qDebug() << "截图 selected";
        }
    });

    connect(aboutAction, &QAction::triggered, this, &Tray::showAbout);    
    connect(quitAction, &QAction::triggered, this, [this](){
        uiaThread->stopMonitoring();
        hookThread->stopMonitoring();
        qApp->quit();
    });

    trayIcon->show();

    // 应用自定义样式
    trayMenu->setStyleSheet("QMenu { background-color: #f0f0f0; border: 1px solid #d0d0d0; font: 12px 'Microsoft YaHei'; }"
                            "QMenu::item { padding: 8px 25px; color: #333; }"
                            "QMenu::item:selected { background-color: #e0e0e0; }"
                            "QCheckBox { margin: 0 10px; spacing: 5px; }"
                            "QCheckBox::indicator { width: 16px; height: 16px; }"
                            "QCheckBox::indicator:unchecked { image: url(:/res/unchecked.png); }"
                            "QCheckBox::indicator:checked { image: url(:/res/checked.png); }"
                            "QCheckBox::indicator:unchecked:hover { image: url(:/res/unchecked_hover.png); }"
                            "QCheckBox::indicator:checked:hover { image: url(:/res/checked_hover.png); }");

    // 创建取词线程
    uiaThread = new UIAThread(selectedPickCheckBox->checkState());
    connect(uiaThread, &UIAThread::textShow, this, &Tray::onTextShow);
    connect(this, &Tray::updateSelectedPick, uiaThread, &UIAThread::updateSelectedPick);
    uiaThread->startMonitoring();

    hookThread = new MouseHookThread();
    connect(hookThread, &MouseHookThread::textShow, this, &Tray::onTextShow);
    hookThread->startMonitoring();
    hookThread->pauseMonitoring();

    // Create single BubbleWindow instance
    bubble = new BubbleWindow(nullptr); // No parent for top-level window
    bubble->hide(); // Initially hidden

    mouseCheckTimer = new QTimer(this);
    connect(mouseCheckTimer, &QTimer::timeout, this, &Tray::checkMouseMovement);

    // 确保程序不因无窗口而退出
    qApp->setQuitOnLastWindowClosed(false);
}

Tray::~Tray()
{
    if (bubble) {
        bubble->close();
        delete bubble;
        bubble = nullptr;
    }
    CoUninitialize();
}

void Tray::showAbout() {
    AboutDialog aboutDialog;
    aboutDialog.exec();
}

void Tray::onTextShow(const QString &text) {
    qDebug() << "onTextShow called with text:" << text;
    if (bubble) {
        bubble->setText(text);
        bubbleShowPos = QCursor::pos();
        qDebug() << "Showing bubble at position:" << bubbleShowPos;
        bubble->move(bubbleShowPos.x() + 7, bubbleShowPos.y() + 7);
        bubble->show();
        mouseCheckTimer->start(50); // Check every 50ms
    }
}

void Tray::checkMouseMovement()
{
    if (bubble && bubble->isVisible()) {
        QPoint currentMousePos = QCursor::pos();
        qDebug() << "Checking mouse: current" << currentMousePos << ", show pos" << bubbleShowPos;
        if (currentMousePos != bubbleShowPos) {
            qDebug() << "Hiding bubble due to mouse movement";
            bubble->hide();
            mouseCheckTimer->stop();
        }
    }
}
