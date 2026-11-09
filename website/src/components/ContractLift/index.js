import React, {useCallback, useEffect, useLayoutEffect, useRef, useState} from 'react';
import styles from './styles.module.css';

// The homepage explainer: the Pledge lines are lifted out of Withdraw into a generated wrapper at build time, the
// call site is intercepted, and a debugger-style execution pointer walks one call through the checks at run time.
// Every step is plain state; the only DOM work is measuring lines for the arrows and the flying copies.

const PROGRAM = [
  {key: 'new', text: 'var account = new Account(100m);'},
  {key: 'site', text: null},
];

const ACCOUNT = [
  {key: 'sig', text: 'public decimal Withdraw(decimal amount)'},
  {key: 'open', text: '{'},
  {key: 'r1', text: '    Pledge.Requires(amount > 0);', pledge: true},
  {key: 'r2', text: '    Pledge.Requires(amount <= Balance);', pledge: true},
  {key: 'e1', text: '    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);', pledge: true},
  {key: 'gap', text: ''},
  {key: 'body', text: '    Balance -= amount;'},
  {key: 'ret', text: '    return Balance;'},
  {key: 'close', text: '}'},
];

const GENERATED = [
  {key: 'il', text: '[InterceptsLocation("Program.cs", line: 2)]'},
  {key: 'sig', text: 'static decimal __Checked(Account @this, decimal amount)'},
  {key: 'open', text: '{'},
  {key: 'r1', text: '    Requires(amount > 0);', lifted: true},
  {key: 'r2', text: '    Requires(amount <= @this.Balance);', lifted: true},
  {key: 'old', text: '    var old = @this.Balance;'},
  {key: 'call', text: '    var result = @this.Withdraw(amount);'},
  {key: 'e1', text: '    Ensures(@this.Balance == old - amount);', lifted: true},
  {key: 'ret', text: '    return result;'},
  {key: 'close', text: '}'},
];

const LIFTED = ['r1', 'r2', 'e1'];

// Below this width the panels cannot sit side by side, so the explainer shows one file at a time, like a debugger
// stepping between files, with no arrows or flying lines.
const COMPACT_WIDTH = 640;

const FILES = [
  ['program', 'Program.cs'],
  ['account', 'Account.cs'],
  ['gen', 'Generated'],
];

const SCENARIOS = {
  bad: {label: 'Bad call', amount: 250},
  good: {label: 'Good call', amount: 40},
  release: {label: 'Release build', amount: 250},
};

const PHASES = [
  ['source', 'Source'],
  ['build', 'Build: lift'],
  ['intercept', 'Build: intercept'],
  ['run', 'Run'],
  ['done', 'Result'],
];

const INITIAL = {
  phase: 'source',
  departed: [],
  landed: [],
  marks: {},
  badges: {},
  ip: null,
  arrow: null,
  site: null,
  outcome: null,
  note: 'Press play to build and run it.',
};

const useIsomorphicLayoutEffect = typeof window === 'undefined' ? useEffect : useLayoutEffect;

const TOKENS = /(\/\/.*$|"[^"]*"|\b(?:public|static|decimal|return|var|new)\b|Pledge\.(?:Requires|Ensures|Old)|\b(?:Requires|Ensures)\b(?=\())/g;

function highlight(text) {
  const parts = [];
  let last = 0;
  for (const match of text.matchAll(TOKENS)) {
    parts.push(text.slice(last, match.index));
    const token = match[0];
    const className = token.startsWith('"')
      ? styles.string
      : /^(public|static|decimal|return|var|new)$/.test(token)
        ? styles.keyword
        : styles.pledge;
    parts.push(<span key={match.index} className={className}>{token}</span>);
    last = match.index + token.length;
  }
  parts.push(text.slice(last));
  return parts;
}

function Line({panel, line, text, className, ip, badge, hidden}) {
  const indent = text.match(/^ */)[0];
  const classes = [styles.line, className, ip && styles.ip, hidden && styles.pending].filter(Boolean).join(' ');
  return (
    <span className={classes} data-line={`${panel}:${line.key}`}>
      <span className={styles.gutter} aria-hidden="true">{ip ? <span className={styles.pointer} /> : null}</span>
      {indent}
      <span data-text>{highlight(text.slice(indent.length))}</span>
      {badge ? <span className={`${styles.badge} ${styles[badge.tone]}`}>{badge.text}</span> : null}
    </span>
  );
}

// A point level with a line, in the root's coordinates: just past its code ('end'), or just outside the left or right
// edge of its panel ('before' or 'after'), so an arrow there clears the gutter's execution pointer.
function anchor(root, id, side) {
  const line = root.querySelector(`[data-line="${id}"]`);
  const text = line?.querySelector('[data-text]');
  if (!text) {
    return null;
  }

  const box = root.getBoundingClientRect();
  const panel = line.closest('[data-code]').getBoundingClientRect();
  const rect = text.getBoundingClientRect();
  const y = rect.top + rect.height / 2 - box.top;
  if (side === 'before') {
    return {x: panel.left - box.left - 5, y};
  }
  if (side === 'after') {
    return {x: panel.right - box.left + 5, y};
  }
  return {x: Math.min(rect.right, panel.right - 6) - box.left + 8, y};
}

function curve(from, to, loopX) {
  if (loopX !== undefined) {
    return `M${from.x},${from.y} C${loopX},${from.y} ${loopX},${to.y} ${to.x},${to.y}`;
  }

  const bend = (to.x - from.x) / 2;
  return `M${from.x},${from.y} C${from.x + bend},${from.y} ${to.x - bend},${to.y} ${to.x},${to.y}`;
}

function measureArrows(root) {
  const program = root.querySelector('[data-code="program"]')?.getBoundingClientRect();
  const generated = root.querySelector('[data-code="gen"]')?.getBoundingClientRect();
  if (!program || !generated || !root.querySelector('[data-line="gen:sig"]')) {
    return null;
  }

  // Side by side, the arrows cross the gap between the columns; stacked, they loop around the right edge. The call
  // site's end starts outside its panel, so the badge it gets on the way back stays clear.
  const stacked = generated.left < program.right;
  const loopX = stacked ? root.getBoundingClientRect().width - 10 : undefined;
  const ends = stacked
    ? [['program:site', 'after', 'gen:sig', 'after'], ['gen:call', 'after', 'account:sig', 'end']]
    : [['program:site', 'after', 'gen:sig', 'before'], ['gen:call', 'before', 'account:sig', 'end']];
  const [intercept, original] = ends.map(([from, fromSide, to, toSide]) => {
    const start = anchor(root, from, fromSide);
    const end = anchor(root, to, toSide);
    return start && end ? curve(start, end, loopX) : null;
  });
  return intercept && original ? {intercept, original} : null;
}

function sameArrows(left, right) {
  return left?.intercept === right?.intercept && left?.original === right?.original;
}

const nextFrame = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));

const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);

// Keyframes for a copy that is picked up, carried along a gentle arc and set down. The arc is sampled so the motion
// has no corner at its peak, and the card's border and shadow fade in and out so it starts and ends looking exactly
// like the line of code underneath it.
function flight(dx, dy, accent, surface) {
  const length = Math.hypot(dx, dy) || 1;
  let nx = dy / length;
  let ny = -dx / length;
  if (ny > 0.2 || (ny > -0.2 && nx < 0)) {
    nx = -nx;
    ny = -ny;
  }

  const bulge = Math.min(40, length * 0.25);
  const frames = [];
  for (let i = 0, count = 32; i <= count; i++) {
    const u = i / count;
    const t = easeInOut(u);
    const curve = 2 * t * (1 - t);
    const raised = Math.min(1, Math.sin(Math.PI * u) * 2.5);
    frames.push({
      offset: u,
      transform: `translate(${dx * t + nx * bulge * curve}px, ${dy * t + ny * bulge * curve}px) scale(${1 + 0.03 * raised})`,
      borderColor: `color-mix(in srgb, ${accent} ${Math.round(raised * 100)}%, transparent)`,
      backgroundColor: `color-mix(in srgb, ${surface} ${Math.round(raised * 100)}%, transparent)`,
      boxShadow: `0 ${(8 * raised).toFixed(2)}px ${(20 * raised).toFixed(2)}px rgb(0 0 0 / ${(0.16 * raised).toFixed(3)})`,
    });
  }
  return frames;
}

export default function ContractLift() {
  const rootRef = useRef(null);
  const runRef = useRef(0);
  const [scenario, setScenario] = useState('bad');
  const [state, setState] = useState(INITIAL);
  const [arrows, setArrows] = useState(null);
  const [compact, setCompact] = useState(false);
  const [viewed, setViewed] = useState(null);
  const compactRef = useRef(false);

  const update = (patch) => setState((current) => ({...current, ...(typeof patch === 'function' ? patch(current) : patch)}));

  // Fly a copy of each contract from Account.cs to its line in the wrapper. The copy carries the same highlighted
  // code and sits exactly over the line it leaves and the line it lands on, so the hand-offs do not show.
  const lift = async (run, sleep, reduced) => {
    const root = rootRef.current;
    if (reduced || compactRef.current) {
      // One file is on screen at a time, so the lines fade out of Withdraw, then fade in to the wrapper.
      for (const [index, key] of LIFTED.entries()) {
        await sleep(index === 0 ? 0 : 350);
        update((current) => ({departed: [...current.departed, key]}));
      }
      await sleep(800);
      for (const [index, key] of LIFTED.entries()) {
        await sleep(index === 0 ? 0 : 350);
        update((current) => ({landed: [...current.landed, key]}));
      }
      return;
    }

    await nextFrame();
    await Promise.all(LIFTED.map(async (key, index) => {
      await sleep(index * 240);
      const from = root.querySelector(`[data-line="account:${key}"]`);
      const to = root.querySelector(`[data-line="gen:${key}"]`);
      const stillRunning = () => run === runRef.current;
      if (!reduced && from && to) {
        const box = root.getBoundingClientRect();
        const origin = {x: box.left + root.clientLeft, y: box.top + root.clientTop};
        const fromText = from.querySelector('[data-text]');
        const start = {x: fromText.getBoundingClientRect().left, y: from.getBoundingClientRect().top};
        const end = {x: to.querySelector('[data-text]').getBoundingClientRect().left, y: to.getBoundingClientRect().top};
        const css = getComputedStyle(root);
        const ghost = document.createElement('span');
        ghost.className = styles.ghost;
        ghost.innerHTML = fromText.innerHTML;
        ghost.style.left = `${start.x - origin.x}px`;
        ghost.style.top = `${start.y - origin.y}px`;
        root.appendChild(ghost);
        update((current) => ({departed: [...current.departed, key]}));
        const animation = ghost.animate(
          flight(end.x - start.x, end.y - start.y, css.getPropertyValue('--lift-accent').trim(), css.getPropertyValue('--lift-surface').trim()),
          {duration: 1250, easing: 'linear', fill: 'forwards'},
        );
        await animation.finished.catch(() => {});
        if (stillRunning()) {
          update((current) => ({landed: [...current.landed, key]}));
          await nextFrame();
        }
        ghost.remove();
        return;
      }

      if (stillRunning()) {
        update((current) => ({departed: [...current.departed, key], landed: [...current.landed, key]}));
      }
    }));
  };

  const play = useCallback((which) => {
    const run = ++runRef.current;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const sleep = (ms) => new Promise((resolve, reject) =>
      setTimeout(() => (run === runRef.current ? resolve() : reject(new Error('cancelled'))), reduced ? 0 : ms));
    const {amount} = SCENARIOS[which];
    const at = (ip, extra = {}) => update((current) => ({ip, ...extra, badges: {...current.badges, ...extra.badges}, marks: extra.marks ?? {}}));
    rootRef.current?.querySelectorAll(`.${styles.ghost}`).forEach((ghost) => ghost.remove());

    const steps = async () => {
      setState(INITIAL);
      await sleep(600);

      if (which === 'release') {
        update({phase: 'build', note: 'Release build: PledgeMode is Ignore, so the Pledge calls compile away.'});
        await sleep(1600);
        update({phase: 'run', note: `Run: Withdraw(${amount}m) on a balance of 100m. Nothing is in the way.`});
        at('program:site');
        await sleep(900);
        at('account:sig');
        await sleep(600);
        at('account:body', {badges: {'account:body': {tone: 'info', text: `Balance = ${100 - amount}`}}});
        await sleep(900);
        at('account:ret');
        await sleep(600);
        at('program:site', {site: 'direct', badges: {'program:site': {tone: 'info', text: `returned ${100 - amount}m`}}});
        update({
          phase: 'done',
          note: '',
          outcome: {tone: 'neutral', title: `Returned ${100 - amount}m`, text: 'Release ran the bad call unchecked. Contracts catch bugs in Debug and in tests; input from users still needs validation.'},
        });
        return;
      }

      update({phase: 'build', note: 'dotnet build: the generator reads the contracts…'});
      await sleep(900);
      update({note: '…and lifts them out of the method into a generated wrapper.'});
      await lift(run, sleep, reduced);
      await sleep(400);
      update({phase: 'intercept', marks: {'gen:il': 'eval'}, note: 'An interceptor reroutes the call site to __Checked. Same C#, standard compiler.'});
      await sleep(1700);

      update({phase: 'run', marks: {}, note: `Run: Withdraw(${amount}m) on a balance of 100m.`});
      at('program:site');
      await sleep(900);
      at('gen:sig', {arrow: 'intercept'});
      await sleep(800);

      const check = async (key, text, ok) => {
        at(`gen:${key}`, {arrow: null, marks: {[`gen:${key}`]: 'eval'}});
        await sleep(650);
        at(`gen:${key}`, {marks: ok ? {} : {[`gen:${key}`]: 'fail'}, badges: {[`gen:${key}`]: {tone: ok ? 'ok' : 'bad', text: `${text} ${ok ? '✓' : '✗'}`}}});
        await sleep(650);
        return ok;
      };

      await check('r1', `${amount} > 0`, amount > 0);
      if (!(await check('r2', `${amount} <= 100`, amount <= 100))) {
        at('program:site', {
          arrow: 'intercept',
          site: 'blame',
          marks: {'gen:r2': 'fail', 'program:site': 'blame'},
          badges: {'program:site': {tone: 'bad', text: "caller's bug"}},
        });
        update({
          phase: 'done',
          note: 'The body never runs. A broken precondition is the caller’s bug.',
          outcome: {tone: 'bad', title: 'PreconditionViolationException', text: 'Precondition of Account.Withdraw violated by its caller: amount <= Balance (at Program.cs:2)'},
        });
        return;
      }

      at('gen:old', {badges: {'gen:old': {tone: 'info', text: 'old = 100'}}});
      await sleep(800);
      at('gen:call');
      await sleep(600);
      at('account:sig', {arrow: 'original'});
      await sleep(700);
      at('account:body', {arrow: null, badges: {'account:body': {tone: 'info', text: `Balance = ${100 - amount}`}}});
      await sleep(900);
      at('account:ret');
      await sleep(600);
      at('gen:call', {arrow: 'original', badges: {'gen:call': {tone: 'info', text: `result = ${100 - amount}`}}});
      await sleep(700);
      await check('e1', `${100 - amount} == 100 − ${amount}`, true);
      at('gen:ret');
      await sleep(600);
      at('program:site', {arrow: 'intercept', site: 'returned', badges: {'program:site': {tone: 'ok', text: `returned ${100 - amount}m`}}});
      update({
        phase: 'done',
        note: 'Every pledge held.',
        outcome: {tone: 'ok', title: `Returned ${100 - amount}m`, text: 'Both preconditions and the postcondition passed. Simple checks like these cost the same as a hand-written guard.'},
      });
    };

    steps().catch((error) => {
      if (error.message !== 'cancelled') {
        throw error;
      }
    });
  }, []);

  // Play once when the explainer first scrolls into view.
  useEffect(() => {
    const root = rootRef.current;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        observer.disconnect();
        play('bad');
      }
    }, {threshold: 0.3});
    observer.observe(root);
    return () => {
      observer.disconnect();
      runRef.current++;
    };
  }, [play]);

  const phaseIndex = PHASES.findIndex(([id]) => id === state.phase);
  const release = scenario === 'release';
  const built = phaseIndex >= 1;
  const wired = !release && phaseIndex >= 2;

  useIsomorphicLayoutEffect(() => {
    const root = rootRef.current;
    const measure = () => {
      const narrow = root.clientWidth < COMPACT_WIDTH;
      compactRef.current = narrow;
      setCompact(narrow);
      const next = wired && !narrow ? measureArrows(root) : null;
      setArrows((current) => (sameArrows(current, next) ? current : next));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(root);
    return () => observer.disconnect();
  }, [wired, state]);

  // In compact mode the file on screen follows the execution pointer, or the build step when there is no pointer.
  // A tapped tab stays on screen until the next step.
  useEffect(() => setViewed(null), [state.ip, state.phase, state.landed.length]);
  const following = state.ip?.split(':')[0]
    ?? (state.phase === 'source' || (state.phase === 'build' && (release || state.landed.length === 0))
      ? 'account'
      : state.phase === 'build' || state.phase === 'intercept' ? 'gen' : 'program');
  const shown = viewed ?? following;
  const panelProps = (file) => (compact ? {hidden: shown !== file} : {});

  const pick = (which) => {
    setScenario(which);
    play(which);
  };

  const lineClass = (id, fallback) => {
    const mark = state.marks[id];
    return mark ? styles[mark] : fallback;
  };

  const siteText = `account.Withdraw(${SCENARIOS[scenario].amount}m);`;

  return (
    <figure className={`${styles.root} ${compact ? styles.compact : ''}`} ref={rootRef} aria-label="How Pledge lifts contracts into a generated wrapper and checks them at run time">
      <div className={styles.controls}>
        <button
          type="button"
          className={styles.play}
          onClick={() => play(scenario)}
          aria-label={state.phase === 'source' ? 'Play' : 'Replay'}
          title={state.phase === 'source' ? 'Play' : 'Replay'}>
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
            <path
              fill="currentColor"
              d={state.phase === 'source'
                ? 'M8 5.5v13a1 1 0 0 0 1.53.85l10.2-6.5a1 1 0 0 0 0-1.7L9.53 4.65A1 1 0 0 0 8 5.5z'
                : 'M17.65 6.35A7.96 7.96 0 0 0 12 4a8 8 0 1 0 7.73 10h-2.08A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h7V4z'}
            />
          </svg>
        </button>
        <div className={styles.segmented} role="group" aria-label="Scenario">
          {Object.entries(SCENARIOS).map(([id, {label}]) => (
            <button key={id} type="button" aria-pressed={scenario === id} onClick={() => pick(id)}>{label}</button>
          ))}
        </div>
      </div>

      <ol className={styles.phases} aria-hidden="true">
        {PHASES.map(([id, label], index) => (
          <li key={id} className={index <= phaseIndex ? styles.phaseOn : undefined}>{label}</li>
        ))}
      </ol>

      {compact ? (
        <div className={styles.files} role="tablist" aria-label="Files">
          {FILES.map(([file, label]) => (
            <button
              key={file}
              type="button"
              role="tab"
              aria-selected={shown === file}
              className={state.ip?.startsWith(`${file}:`) ? styles.here : undefined}
              onClick={() => setViewed(file)}>
              {label}
            </button>
          ))}
        </div>
      ) : null}

      <div className={styles.columns}>
        <div className={styles.column}>
          <div className={styles.panel} {...panelProps('program')}>
            <span className={styles.tab}>Program.cs</span>
            <div className={styles.code} data-code="program">
              {PROGRAM.map((line) => {
                const id = `program:${line.key}`;
                return (
                  <Line key={line.key} panel="program" line={line} text={line.text ?? siteText}
                    className={lineClass(id)} ip={state.ip === id} badge={state.badges[id]} />
                );
              })}
            </div>
          </div>
          <div className={styles.panel} {...panelProps('account')}>
            <span className={styles.tab}>Account.cs</span>
            <div className={styles.code} data-code="account">
              {ACCOUNT.map((line) => {
                const id = `account:${line.key}`;
                const gone = line.pledge && built && (release || state.departed.includes(line.key));
                const lifting = line.pledge && built && !gone;
                return (
                  <Line key={line.key} panel="account" line={line} text={line.text}
                    className={lineClass(id, gone ? styles.gone : lifting ? styles.lifting : undefined)}
                    ip={state.ip === id} badge={state.badges[id]} />
                );
              })}
            </div>
          </div>
        </div>

        <div className={styles.column}>
          <div className={styles.panel} {...panelProps('gen')}>
            <span className={styles.tab}><span className={styles.generated}>generated</span> Account_Withdraw.g.cs</span>
            <div className={styles.code} data-code="gen">
              {!built
                ? <span className={styles.placeholder}>Nothing yet. The build writes this file.</span>
                : release
                  ? <span className={styles.placeholder}>Nothing generated. In Release the mode is Ignore, so the assembly is byte-identical to one built without Pledge.</span>
                  : GENERATED.map((line) => {
                    const id = `gen:${line.key}`;
                    const arrived = line.lifted && compact && state.landed.includes(line.key);
                    return (
                      <Line key={line.key} panel="gen" line={line} text={line.text}
                        className={lineClass(id, !line.lifted || arrived ? styles.fresh : undefined)}
                        hidden={line.lifted && !state.landed.includes(line.key)}
                        ip={state.ip === id} badge={state.badges[id]} />
                    );
                  })}
            </div>
          </div>
          <p className={styles.note} aria-live="polite">{state.note}</p>
          {state.outcome ? (
            <div className={`${styles.outcome} ${styles[state.outcome.tone]}`}>
              <strong>{state.outcome.title}</strong>
              <span>{state.outcome.text}</span>
            </div>
          ) : null}
        </div>
      </div>

      {arrows ? (
        <svg className={styles.arrows} aria-hidden="true">
          <defs>
            <marker id="pledge-lift-head" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M0,0 L10,5 L0,10 z" className={styles.head} />
            </marker>
            <marker id="pledge-lift-head-active" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M0,0 L10,5 L0,10 z" className={styles.headActive} />
            </marker>
          </defs>
          {['intercept', 'original'].map((key) => {
            const active = state.arrow === key;
            return (
              <path
                key={key}
                d={arrows[key]}
                className={active ? styles.arrowActive : styles.arrow}
                markerEnd={`url(#${active ? 'pledge-lift-head-active' : 'pledge-lift-head'})`}
              />
            );
          })}
        </svg>
      ) : null}
    </figure>
  );
}
