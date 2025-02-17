﻿/****************************************************************************
**
** Copyright (C) 2018 The Qt Company Ltd.
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

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace QtVsTools.VisualStudio
{
    using ServiceType = Tuple<Type, Type>;

    public interface IVsServiceProvider
    {
        I GetService<T, I>() where T : class where I : class;
        Task<I> GetServiceAsync<T, I>() where T : class where I : class;
    }

    public static class VsServiceProvider
    {
        public static IVsServiceProvider Instance { get; set; }

        static readonly ConcurrentDictionary<ServiceType, object> services
            = new ConcurrentDictionary<ServiceType, object>();

        public static I GetService<I>()
            where I : class
        {
            return GetService<I, I>();
        }

        public static I GetService<T, I>()
            where T : class
            where I : class
        {
            if (Instance == null)
                return null;

            if (services.TryGetValue(new ServiceType(typeof(T), typeof(I)), out object serviceObj))
                return serviceObj as I;

            var serviceInterface = Instance.GetService<T, I>();
            services.TryAdd(new ServiceType(typeof(T), typeof(I)), serviceInterface);
            return serviceInterface;
        }

        public static async Task<I> GetServiceAsync<I>()
            where I : class
        {
            return await GetServiceAsync<I, I>();
        }

        public static async Task<I> GetServiceAsync<T, I>()
            where T : class
            where I : class
        {
            if (Instance == null)
                return null;

            if (services.TryGetValue(new ServiceType(typeof(T), typeof(I)), out object serviceObj))
                return serviceObj as I;

            var serviceInterface = await Instance.GetServiceAsync<T, I>();
            services.TryAdd(new ServiceType(typeof(T), typeof(I)), serviceInterface);
            return serviceInterface;
        }
    }
}
