﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.CodeAnalysis.Text;
using TextChangeEventArgs = Microsoft.CodeAnalysis.Text.TextChangeEventArgs;

namespace RoslynPad.Editor.Windows
{
    public sealed class AvalonEditTextContainer : SourceTextContainer, IDisposable
    {
        private readonly TextDocument _document;

        private SourceText _currentText;
        private bool _updatding;

        public TextDocument Document => _document;

        /// <summary>
        /// If set, <see cref="TextEditor.CaretOffset"/> will be updated.
        /// </summary>
        public TextEditor Editor { get; set; }

        public override SourceText CurrentText => _currentText;

        public AvalonEditTextContainer(TextDocument document)
        {
            _document = document;
            _currentText = new AvalonEditSourceText(this, _document.Text);

            _document.Changed += DocumentOnChanged;
        }

        public void Dispose()
        {
            _document.Changed -= DocumentOnChanged;
        }

        private void DocumentOnChanged(object sender, DocumentChangeEventArgs e)
        {
            if (_updatding) return;

            var oldText = _currentText;

            var textSpan = new TextSpan(e.Offset, e.RemovalLength);
            var textChangeRange = new TextChangeRange(textSpan, e.InsertionLength);
            _currentText = _currentText.WithChanges(new TextChange(textSpan, e.InsertedText?.Text ?? string.Empty));

            TextChanged?.Invoke(this, new TextChangeEventArgs(oldText, _currentText, textChangeRange));
        }

        public int a;

        public override event EventHandler<TextChangeEventArgs> TextChanged;

        public void UpdateText(SourceText newText)
        {
            _updatding = true;
            _document.BeginUpdate();
            var editor = Editor;
            var caret = editor?.CaretOffset ?? 0;
            var caretOffset = caret;
            var documentOffset = 0;
            try
            {
                var changes = newText.GetTextChanges(_currentText);
                
                foreach (var change in changes)
                {
                    _document.Replace(change.Span.Start + documentOffset, change.Span.Length, new StringTextSource(change.NewText));

                    var changeOffset = change.NewText.Length - change.Span.Length;
                    if (caret >= change.Span.Start + documentOffset + change.Span.Length)
                    {
                        // If caret is after text, adjust it by text size difference
                        caret += changeOffset;
                    }
                    else if (caret >= change.Span.Start + documentOffset)
                    {
                        // If caret is inside changed text, but go out of bounds of the replacing text after the change, go back inside
                        if (caret >= change.Span.Start + documentOffset + change.NewText.Length)
                        {
                            caret = change.Span.Start + documentOffset;
                        }
                    }
                    documentOffset += changeOffset;
                }

                _currentText = newText;
            }
            finally
            {
                _updatding = false;
                if (caretOffset < 0)
                    caretOffset = 0;
                if (caretOffset > newText.Length)
                    caretOffset = newText.Length;
                if (editor != null)
                    editor.CaretOffset = caretOffset;
                _document.EndUpdate();
            }
        }

        private class AvalonEditSourceText : SourceText
        {
            private readonly AvalonEditTextContainer _container;
            private readonly SourceText _sourceText;

            public AvalonEditSourceText(AvalonEditTextContainer container, string text) : this(container, From(text))
            {
            }

            private AvalonEditSourceText(AvalonEditTextContainer container, SourceText sourceText)
            {
                _container = container;
                _sourceText = sourceText;
            }

            public override void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
            {
                _sourceText.CopyTo(sourceIndex, destination, destinationIndex, count);
            }

            public override Encoding Encoding => _sourceText.Encoding;

            public override int Length => _sourceText.Length;

            public override char this[int position] => _sourceText[position];

            public override SourceText GetSubText(TextSpan span) => new AvalonEditSourceText(_container, _sourceText.GetSubText(span));

            public override void Write(TextWriter writer, TextSpan span, CancellationToken cancellationToken = new CancellationToken())
            {
                _sourceText.Write(writer, span, cancellationToken);
            }

            public override string ToString() => _sourceText.ToString();

            public override string ToString(TextSpan span) => _sourceText.ToString(span);
            
            public override IReadOnlyList<TextChangeRange> GetChangeRanges(SourceText oldText)
                => _sourceText.GetChangeRanges(GetInnerSourceText(oldText));

            public override IReadOnlyList<TextChange> GetTextChanges(SourceText oldText) => _sourceText.GetTextChanges(GetInnerSourceText(oldText));

            protected override TextLineCollection GetLinesCore() => _sourceText.Lines;

            protected override bool ContentEqualsImpl(SourceText other) => _sourceText.ContentEquals(GetInnerSourceText(other));

            public override SourceTextContainer Container => _container ?? _sourceText.Container;

            public override bool Equals(object obj) => _sourceText.Equals(obj);

            public override int GetHashCode() => _sourceText.GetHashCode();

            public override SourceText WithChanges(IEnumerable<TextChange> changes)
            {
                return new AvalonEditSourceText(_container, _sourceText.WithChanges(changes));
            }

            private static SourceText GetInnerSourceText(SourceText oldText)
            {
                return (oldText as AvalonEditSourceText)?._sourceText ?? oldText;
            }
        }
    }
}
