/****************************************************************************
**
** Copyright (C) 2016 The Qt Company Ltd.
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

using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using QtVsTools.Core;
using QtVsTools.VisualStudio;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;

namespace QtVsTools.Wizards.Util
{
    internal class FileExistsinFilterValidationRule : VCLanguageManagerValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value is string) {
                var dte = VsServiceProvider.GetService<SDTE, DTE>();
                if (dte == null)
                    return ValidationResult.ValidResult;

                var project = HelperFunctions.GetSelectedProject(dte);
                if (project == null)
                    return ValidationResult.ValidResult;

                var files = HelperFunctions.GetProjectFiles(project, Filter);
                if (files.Count == 0)
                    return ValidationResult.ValidResult;

                var fileName = (value as string).ToUpperInvariant();
                if (files.FirstOrDefault(x => x.ToUpperInvariant() == fileName) != null)
                    return new ValidationResult(false, @"File already exists.");
                return ValidationResult.ValidResult;
            }
            return new ValidationResult(false, @"Invalid file name.");
        }

        public FilesToList Filter { get; set; }
    }
}
