﻿using Files.Filesystem;
using Files.Interacts;
using Files.UserControls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace Files
{
    public interface IShellPage
    {
        public Frame ContentFrame { get; }
        public Interaction InteractionOperations { get; }
        public ItemViewModel ViewModel { get; }
        public BaseLayout ContentPage { get; }
        public Control OperationsControl { get; }
        public Type CurrentPageType { get; }
        public INavigationToolbar NavigationControl { get; }
    }
}
