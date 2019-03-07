/****************************************************************************
**
** Copyright (C) 2019 The Qt Company Ltd.
** Contact: https://www.qt.io/licensing/
**
** This file is part of the Qt VS Tools.
**
** $QT_BEGIN_LICENSE:GPL-EXCEPT$
** Commercial License Usage
** Licensees holding valid commercial Qt licenses may use this file in
** accordance with the commercial license agreement provided with the
** Software or, alternatively, in accordance with the terms contained in
** a written agreement between you and The Qt Company. For licensing terms
** and conditions see https://www.qt.io/terms-conditions. For further
** information use the contact form at https://www.qt.io/contact-us.
**
** GNU General Public License Usage
** Alternatively, this file may be used under the terms of the GNU
** General Public License version 3 as published by the Free Software
** Foundation with exceptions as appearing in the file LICENSE.GPL3-EXCEPT
** included in the packaging of this file. Please review the following
** information to ensure the GNU General Public License requirements will
** be met: https://www.gnu.org/licenses/gpl-3.0.html.
**
** $QT_END_LICENSE$
**
****************************************************************************/

#ifndef MACROCLIENT_H
#define MACROCLIENT_H

#include <QElapsedTimer>
#include <QtNetwork>
#include <QProcess>

class MacroClient
{

private:
    QProcess vsProcess;
    QLocalSocket socket;

public:
    MacroClient()
    {
        vsProcess.setProgram("devenv.exe");
        vsProcess.setArguments({ "/rootsuffix", "Exp" });
        vsProcess.setWorkingDirectory(QDir::currentPath());
    }

    ~MacroClient()
    {
    }

    bool connectToServer()
    {
        if (vsProcess.state() != QProcess::Running) {
            vsProcess.start();
            if (!vsProcess.waitForStarted())
                return false;
        }

        QString pipeName = QStringLiteral("QtVSTest_%1").arg(vsProcess.processId());

        QElapsedTimer timer;
        timer.start();

        while (!timer.hasExpired(30000) && socket.state() != QLocalSocket::ConnectedState) {
            socket.connectToServer(pipeName, QIODevice::ReadWrite);
            if (socket.state() != QLocalSocket::ConnectedState) {
                socket.abort();
                QThread::usleep(100);
            }
        }

        return (socket.state() == QLocalSocket::ConnectedState);
    }

    bool disconnectFromServer(bool closeVs)
    {
        if (vsProcess.state() != QProcess::Running)
            return true;

        if (socket.state() == QLocalSocket::ConnectedState)
            socket.disconnectFromServer();

        if (closeVs)
            return terminateServerProcess();

        return true;
    }

    bool terminateServerProcess()
    {
        if (vsProcess.state() != QProcess::Running)
            return true;

        vsProcess.terminate();
        if (!vsProcess.waitForFinished(3000)) {
            vsProcess.kill();
            if (!vsProcess.waitForFinished(7000))
                return false;
        }

        return true;
    }

    QString runMacro(QString macroCode)
    {
        if (socket.state() != QLocalSocket::ConnectedState && !connectToServer())
            return QStringLiteral("(error)\r\nDisconnected");

        QByteArray data = macroCode.toUtf8();
        int size = data.size();

        socket.write(reinterpret_cast<const char *>(&size), sizeof(int));
        socket.write(data);
        socket.flush();

        if (socket.state() != QLocalSocket::ConnectedState)
            return QStringLiteral("(error)\r\nDisconnected");

        while (socket.state() == QLocalSocket::ConnectedState && socket.bytesToWrite())
            socket.waitForBytesWritten(15000);
        if (socket.state() != QLocalSocket::ConnectedState)
            return QStringLiteral("(error)\r\nDisconnected");

        while (socket.state() == QLocalSocket::ConnectedState && socket.bytesAvailable() < 4)
            socket.waitForReadyRead(15000);
        if (socket.state() != QLocalSocket::ConnectedState)
            return QStringLiteral("(error)\r\nDisconnected");

        size = *reinterpret_cast<int *>(socket.read(4).data());

        while (socket.state() == QLocalSocket::ConnectedState && socket.bytesAvailable() < size)
            socket.waitForReadyRead(15000);
        if (socket.state() != QLocalSocket::ConnectedState)
            return QStringLiteral("(error)\r\nDisconnected");

        data = socket.read(size);
        return QString::fromUtf8(data);
    }

}; // class MacroClient

#endif // MACROCLIENT_H
