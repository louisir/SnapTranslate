QT = core gui svg
greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

CONFIG += c++17 windows
# 彻底禁用Qt的默认清单处理
CONFIG -= embed_manifest_dll
CONFIG -= embed_manifest_exe

# You can make your code fail to compile if it uses deprecated APIs.
# In order to do so, uncomment the following line.
#DEFINES += QT_DISABLE_DEPRECATED_BEFORE=0x060000    # disables all the APIs deprecated before Qt 6.0.0

SOURCES += \
        bubblewindow.cpp \
        main.cpp \
        mousehookthread.cpp \
        tray.cpp \
        uiathread.cpp

# Default rules for deployment.
qnx: target.path = /tmp/$${TARGET}/bin
else: unix:!android: target.path = /opt/$${TARGET}/bin
!isEmpty(target.path): INSTALLS += target

HEADERS += \
    bubblewindow.h \
    mousehookthread.h \
    tray.h \
    uiathread.h

DISTFILES += \
    swc.manifest \
    swc.rc

RESOURCES += \
    res.qrc

RC_FILE = swc.rc

win32 {
    LIBS     += -luser32 -lole32 -loleaut32 -lUIAutomationCore

    # 确保使用 Unicode 字符集
    QMAKE_CFLAGS += /utf-8
    QMAKE_CXXFLAGS += /utf-8
}
