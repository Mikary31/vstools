/****************************************************************************
**
** Copyright (C) 2022 The Qt Company Ltd.
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
using System.Collections;
using System.Collections.Generic;
using System.Windows.Navigation;

namespace QtVsTools.Wizards.Common
{
    using Core;

    public partial class WizardWindow : NavigationWindow, IEnumerable<WizardPage>
    {

        public WizardWindow(IEnumerable<WizardPage> pages = null, string title = null)
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;

            if (title != null)
                Title = title;

            Pages = new List<WizardPage>();

            if (pages != null) {
                foreach (var page in pages)
                    Add(page);
            }
        }

        public void Add(WizardPage page)
        {
            bool isFirstPage = (Pages.Count == 0);
            page.Wizard = this;
            page.NavigateForward += OnNavigateForward;
            page.NavigatedBackward += OnNavigatedBackwards;
            Pages.Add(page);

            if (isFirstPage) {
                NextPage.ReturnEx += OnPageReturn;
                Navigate(NextPage); // put on navigation stack
            }
        }

        public WizardPage NextPage => Pages[currentPage];

        private List<WizardPage> Pages
        {
            get;
        }

        public IEnumerator<WizardPage> GetEnumerator()
        {
            return ((IEnumerable<WizardPage>)Pages).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<WizardPage>)Pages).GetEnumerator();
        }

        private int currentPage;

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var windowStyles = NativeAPI.GetWindowLong(hwnd, NativeAPI.GWL_STYLE);
                NativeAPI.SetWindowLong(hwnd, NativeAPI.GWL_STYLE,
                    windowStyles & ~(NativeAPI.WS_MAXIMIZEBOX | NativeAPI.WS_MINIMIZEBOX));
            } catch {
                // Ignore if we can't remove the minimize and maximize buttons.
                SourceInitialized -= OnSourceInitialized;
            }
        }

        private void OnNavigateForward(object sender, EventArgs e)
        {
            var tmp = currentPage + 1;
            if (tmp >= Pages.Count) {
                throw new InvalidOperationException(@"Current wizard page "
                    + @"cannot be equal or greater than pages count.");
            }
            currentPage++;
        }

        private void OnPageReturn(object sender, ReturnEventArgs<WizardResult> e)
        {
            if (DialogResult == null)
                DialogResult = (e.Result == WizardResult.Finished);
        }

        private void OnNavigatedBackwards(object sender, EventArgs e)
        {
            var tmp = currentPage - 1;
            if (tmp < 0)
                throw new InvalidOperationException(@"Current wizard page cannot be less then 0.");
            currentPage--;
        }
    }
}
