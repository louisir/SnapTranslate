#ifndef BUBBLEWINDOW_H
#define BUBBLEWINDOW_H

#include <QWidget>
#include <QString>
#include <QPainter>
#include <QGraphicsDropShadowEffect>

class BubbleWindow : public QWidget
{
    Q_OBJECT

public:
    BubbleWindow(QWidget *parent = nullptr);
    void setText(const QString &text);

protected:
    void paintEvent(QPaintEvent *event) override;

private:
    QString displayText;
};

#endif // BUBBLEWINDOW_H
