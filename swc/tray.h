#ifndef TRAY_H
#define TRAY_H

#include <QObject>
#include <QWidget>
#include <QSystemTrayIcon>
#include <QMenu>
#include <QAction>
#include <QTimer>
#include <QSettings>
#include <QClipboard>
#include <QDialog>
#include <QLabel>
#include <QWidgetAction>
#include <QCheckBox>

#include <Windows.h>
#include <UIAutomation.h>

#include "bubblewindow.h"
#include "uiathread.h"
#include "mousehookthread.h"

class AboutDialog : public QDialog {
    Q_OBJECT
public:
    explicit AboutDialog(QWidget *parent = nullptr);
};

class Tray : public QObject
{
    Q_OBJECT
public:
    explicit Tray(QObject *parent = nullptr);
    ~Tray();

signals:
    void updateSelectedPick(const bool &selected);

private slots:    
    void showAbout();

    void onTextShow(const QString &text);

    void checkMouseMovement();

private:
    QSystemTrayIcon *trayIcon;
    QMenu *trayMenu;

    QWidgetAction* dockPickAction;
    QCheckBox* dockPickCheckBox;

    QWidgetAction* selectedPickAction;
    QCheckBox* selectedPickCheckBox;

    QWidgetAction* screenshotAction;
    QCheckBox* screenshotCheckBox;

    QAction *aboutAction;
    QAction *quitAction;

    BubbleWindow *bubble;

    UIAThread* uiaThread;
    MouseHookThread* hookThread;

    QPoint bubbleShowPos; // Position when bubble was shown
    QTimer *mouseCheckTimer;
};

#endif // TRAY_H
