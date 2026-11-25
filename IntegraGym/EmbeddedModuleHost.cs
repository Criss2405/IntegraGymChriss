namespace IntegraGym;

// A module can keep the workspace open while its asynchronous operation completes.
internal interface IWorkspaceActivity { bool IsBusy { get; } }

internal sealed class EmbeddedModuleHost : Panel
{
 public Form Module { get; }
 private readonly Size minimumContent;
 public EmbeddedModuleHost(Form module)
 {
  Module=module; Dock=DockStyle.Fill;AutoScroll=true;BackColor=Night.Canvas;
  module.WindowState=FormWindowState.Normal;
  minimumContent=new Size(Math.Max(module.ClientSize.Width,module.MinimumSize.Width),Math.Max(module.ClientSize.Height,module.MinimumSize.Height));
  module.TopLevel=false;module.ShowInTaskbar=false;module.FormBorderStyle=FormBorderStyle.None;
  module.MinimumSize=Size.Empty;module.Dock=DockStyle.None;module.Anchor=AnchorStyles.Top|AnchorStyles.Left;
  module.Location=Point.Empty;Controls.Add(module);Resize+=(_,_)=>Fit();Fit();
 }
 private void Fit()
 {
  if(Module.IsDisposed)return;
  // Reserve scrollbar space so small displays can reach every field and action.
  Module.Size=new Size(Math.Max(minimumContent.Width,ClientSize.Width-SystemInformation.VerticalScrollBarWidth),Math.Max(minimumContent.Height,ClientSize.Height-SystemInformation.HorizontalScrollBarHeight));
  AutoScrollMinSize=Module.Size;
 }
 public void ActivateModule(){Fit();Module.Show();Module.BringToFront();}
}


