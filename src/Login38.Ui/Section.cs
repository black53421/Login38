using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Login38.Ui;

/// <summary>
/// A titled group of settings.
/// </summary>
/// <remarks>
/// <para>
/// Both applications are lists of settings in groups, and before this every group was a
/// heading text block, a caption text block and a margin, written out again for each of the
/// forty-odd groups. That is why they had drifted: a heading here with eight pixels under
/// it and one there with four, a caption on some groups and not on others.
/// </para>
/// <para>
/// A control rather than a style, because a group is three things that have to stay in a
/// fixed relationship, and a style can only set properties on one.
/// </para>
/// </remarks>
public class Section : HeaderedContentControl
{
    /// <summary>The sentence under the title saying what the group is for.</summary>
    /// <remarks>Optional. A group whose title says everything is drawn without one.</remarks>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
        nameof(Note), typeof(string), typeof(Section), new PropertyMetadata(string.Empty));

    public string Note
    {
        get => (string)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
}

/// <summary>
/// One labelled setting: a name on the left, the control that changes it on the right.
/// </summary>
/// <remarks>
/// The other repeated shape. Keeping the label column a fixed width across a page is what
/// lets the eye run down the controls rather than hunting for each one at whatever
/// indent its label happened to end at.
/// </remarks>
public class FieldRow : HeaderedContentControl
{
    /// <summary>The sentence under the label, or nothing.</summary>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
        nameof(Note), typeof(string), typeof(FieldRow), new PropertyMetadata(string.Empty));

    /// <summary>How wide the label column is.</summary>
    /// <remarks>
    /// Settable because the two applications have different longest labels and a width
    /// that suits one wastes a third of the other's width.
    /// </remarks>
    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
        nameof(LabelWidth), typeof(double), typeof(FieldRow), new PropertyMetadata(120d));

    public string Note
    {
        get => (string)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public double LabelWidth
    {
        get => (double)GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }
}

/// <summary>
/// A small filled circle saying what state something is in.
/// </summary>
/// <remarks>
/// The launcher's server list and the helper's own status line both need "is this thing
/// alive", and a word for it takes a column that neither has. A dot with a glow reads at a
/// glance and, unlike a coloured word, does not need to be translated.
/// </remarks>
public class StatusDot : Control
{
    /// <summary>What colour the dot is.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(StatusDot), new PropertyMetadata(Brushes.Gray));

    /// <summary>
    /// Whether it glows.
    /// </summary>
    /// <remarks>
    /// Off for anything the state of which is "nothing is happening". A glow is what marks
    /// the live one out of a list, and a list where every dot glows has marked out none.
    /// </remarks>
    public static readonly DependencyProperty IsLitProperty = DependencyProperty.Register(
        nameof(IsLit), typeof(bool), typeof(StatusDot), new PropertyMetadata(false));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool IsLit
    {
        get => (bool)GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }
}
