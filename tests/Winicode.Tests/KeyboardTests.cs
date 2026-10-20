namespace Winicode;

internal static class KeyboardTests
{
    internal static IReadOnlyList<string> Run()
    {
        var failures=new List<string>();var b=new CompositionBuffer();
        void Expect(string value,string label){if(b.Text!=value)failures.Add($"{label}: expected {value}, got {b.Text}");}
        foreach(var c in "hello")b.Letter(c);Expect("hello","initial preedit");
        b.Backspace();b.Letter('p');Expect("hellp","backspace");
        b.Left();b.Backspace();b.Letter('l');Expect("hellp","caret editing");
        b.Delete();Expect("hell","delete");
        b.Clear();Expect("","reset");
        for(var i=0;i<129;i++)b.Letter('a');Expect("","bounded buffer");
        return failures;
    }
}
