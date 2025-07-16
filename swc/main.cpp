#include <QApplication>
#include <objbase.h>

#include "tray.h"

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "psapi.lib")

int main(int argc, char *argv[])
{
    QApplication a(argc, argv);
    Tray swcTray;
    return a.exec();
}
