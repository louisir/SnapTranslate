#include "bubblewindow.h"
#include <QApplication>
#include <QScreen>
#include <QMouseEvent>

BubbleWindow::BubbleWindow(QWidget *parent)
    : QWidget(parent), displayText("")
{
    // Use Qt::Tool to avoid taskbar icon, keep FramelessWindowHint for borderless look
    setWindowFlags(Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint);
    setAttribute(Qt::WA_TranslucentBackground);

    // Add shadow effect
    QGraphicsDropShadowEffect *shadow = new QGraphicsDropShadowEffect(this);
    shadow->setBlurRadius(15);
    shadow->setColor(QColor(0, 0, 0, 100));
    shadow->setOffset(0, 0);
    setGraphicsEffect(shadow);

    // Initial size (will be updated by setText)
    setFixedSize(100, 50);
}

void BubbleWindow::setText(const QString &text)
{
    displayText = text;
    // Calculate size based on text
    QFontMetrics fm(font());
    int textWidth = fm.horizontalAdvance(text) + 20;
    int textHeight = fm.height() + 20;
    setFixedSize(textWidth, textHeight);
    update(); // Trigger repaint
}

void BubbleWindow::paintEvent(QPaintEvent *)
{
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);

    // Draw background
    QRectF rect(0, 0, width(), height());
    painter.setPen(Qt::NoPen);
    QLinearGradient gradient(0, 0, 0, height());
    gradient.setColorAt(0, QColor(50, 100, 200, 200));
    gradient.setColorAt(1, QColor(20, 50, 150, 200));
    painter.setBrush(gradient);
    painter.drawRoundedRect(rect, 10, 10);

    // Draw border
    painter.setPen(QPen(QColor(100, 150, 255, 200), 1));
    painter.setBrush(Qt::NoBrush);
    painter.drawRoundedRect(rect.adjusted(0.5, 0.5, -0.5, -0.5), 10, 10);

    // Draw text
    painter.setPen(Qt::white);
    painter.drawText(rect.adjusted(10, 10, -10, -10), Qt::AlignCenter, displayText);
}
