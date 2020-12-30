local Ent = AnimatedEntity();

Ent.SetModel("snoutx10k/snoutx10k.iqm");

Ent.RegisterAnimation("idle", "snoutx10k/idle.md5anim.iqm");
Ent.RegisterAnimation("forward", "snoutx10k/forward.md5anim.iqm");
Ent.RegisterAnimation("right", "snoutx10k/right.md5anim.iqm");
Ent.RegisterAnimation("left", "snoutx10k/left.md5anim.iqm");
Ent.RegisterAnimation("jump", "snoutx10k/jump.md5anim.iqm");
Ent.RegisterAnimation("backward", "snoutx10k/backward.md5anim.iqm");

Ent.RegisterAnimation("hold1", "snoutx10k/chainsaw_idle.md5anim.iqm");
Ent.RegisterAnimation("hold2", "snoutx10k/shotgun_idle.md5anim.iqm");
Ent.RegisterAnimation("hold3", "snoutx10k/minigun_idle.md5anim.iqm");
Ent.RegisterAnimation("hold4", "snoutx10k/rl_idle.md5anim.iqm");
Ent.RegisterAnimation("hold5", "snoutx10k/sniper_idle.md5anim.iqm");
Ent.RegisterAnimation("hold6", "snoutx10k/gl_idle.md5anim.iqm");
Ent.RegisterAnimation("hold7", "snoutx10k/idle.md5anim.iqm");

Ent.RegisterAnimation("attack1", "snoutx10k/chainsaw_attack.md5anim.iqm");
Ent.RegisterAnimation("attack2", "snoutx10k/shotgun_shoot.md5anim.iqm");
Ent.RegisterAnimation("attack3", "snoutx10k/minigun_shoot.md5anim.iqm");
Ent.RegisterAnimation("attack4", "snoutx10k/rl_shoot.md5anim.iqm");
Ent.RegisterAnimation("attack5", "snoutx10k/sniper_shoot.md5anim.iqm");
Ent.RegisterAnimation("attack6", "snoutx10k/gl_shoot.md5anim.iqm");
Ent.RegisterAnimation("attack7", "snoutx10k/shoot.md5anim.iqm");

Ent.RegisterAnimation("win", "snoutx10k/win.md5anim.iqm");
Ent.RegisterAnimation("sink", "snoutx10k/sink.md5anim.iqm");
Ent.RegisterAnimation("lose", "snoutx10k/lose.md5anim.iqm");
Ent.RegisterAnimation("edit", "snoutx10k/edit.md5anim.iqm");
Ent.RegisterAnimation("lag", "snoutx10k/lag.md5anim.iqm");
Ent.RegisterAnimation("punch", "snoutx10k/punch.md5anim.iqm");
Ent.RegisterAnimation("swim", "snoutx10k/swim.md5anim.iqm");
Ent.RegisterAnimation("taunt", "snoutx10k/taunt.md5anim.iqm");

Ent.RegisterAnimation("dying", "snoutx10k/dying.md5anim.iqm");
Ent.RegisterAnimation("dying", "snoutx10k/dying2.md5anim.iqm");

Ent.RegisterAnimation("dead", "snoutx10k/dead.md5anim.iqm");
Ent.RegisterAnimation("dead", "snoutx10k/dead2.md5anim.iqm");

Ent.RegisterAnimation("pain", "snoutx10k/pain.md5anim.iqm");
Ent.RegisterAnimation("pain", "snoutx10k/pain2.md5anim.iqm");

Ent.SetMeshTexture(0, "snoutx10k/lower.png");
Ent.SetMeshTexture(1, "snoutx10k/upper.png");
Ent.UpperBodyMesh = 1;

return Ent;