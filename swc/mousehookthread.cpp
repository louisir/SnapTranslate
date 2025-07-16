#include "mousehookthread.h"

MouseHookThread* MouseHookThread::instance = nullptr;
QMutex MouseHookThread::instanceMutex;
