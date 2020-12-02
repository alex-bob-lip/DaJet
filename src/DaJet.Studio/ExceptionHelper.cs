﻿using System;
using System.Windows;

namespace DaJet.Studio
{
    public static class ExceptionHelper
    {
        private const string DAJET_WINDOW_CAPTION = "DaJet";
        public static string GetErrorText(Exception ex)
        {
            string errorText = string.Empty;
            Exception error = ex;
            while (error != null)
            {
                errorText += (errorText == string.Empty) ? error.Message : Environment.NewLine + error.Message;
                error = error.InnerException;
            }
            return errorText;
        }
        public static void ShowException(Exception ex)
        {
            _ = MessageBox.Show(GetErrorText(ex), DAJET_WINDOW_CAPTION, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}