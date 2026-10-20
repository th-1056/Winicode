namespace Winicode;

// Bounded, transient editing state. No keystrokes are persisted.
internal sealed class CompositionBuffer
{
    private string text="";
    private int caret;
    internal string Text=>text;
    internal void Clear(){text="";caret=0;}
    internal void Letter(char value)
    {
        if(text.Length>=128){Clear();return;}
        text=text.Insert(caret,value.ToString());caret++;
    }
    internal void Backspace(){if(caret>0){text=text.Remove(caret-1,1);caret--;}}
    internal void Delete(){if(caret<text.Length)text=text.Remove(caret,1);}
    internal void Left()=>caret=Math.Max(0,caret-1);
    internal void Right()=>caret=Math.Min(text.Length,caret+1);
}
