import React from 'react';
import Link from '@docusaurus/Link';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';
import Layout from '@theme/Layout';
import ContractLift from '@site/src/components/ContractLift';
import styles from './index.module.css';

const ENSURES = `public decimal Withdraw(decimal amount)
{
    Pledge.Requires(amount > 0);
    Pledge.Requires(amount <= Balance, "insufficient funds");
    Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);

    Balance -= amount;
    return Balance;
}`;

const PROPERTY_TEST = `[Fact]
public void Account_pledges_hold() =>
    PledgeProperties.Check<Account>(iterations: 1_000);`;

const PROPERTY_FAILURE = `RoundingAccount.Withdraw(decimal) broke its pledge. Postcondition of RoundingAccount.Withdraw violated: Balance == Old(Balance) - amount
Counterexample:
  receiver = new RoundingAccount(542m)
  amount = 0.00000000000000000001701m`;

const BLAME = `Precondition of Account.Withdraw violated by its caller: insufficient funds [amount <= Balance] (at /src/Program.cs:9)`;

const MODES = `<PropertyGroup>
  <!-- Ignore, Observe, Enforce or EnforceFailFast -->
  <PledgeMode>Observe</PledgeMode>
</PropertyGroup>`;

const REASONS = [
  {
    title: 'Find bugs where they start',
    body: "Most bugs don't throw. They leave something a little wrong, and it shows up somewhere else later. A contract fails at the call that broke the rule.",
  },
  {
    title: 'Check every call, not a few examples',
    body: "Unit tests check the inputs you thought of. Contracts check every call while you run and debug, and Pledge can turn them into property tests for the inputs you didn't.",
  },
  {
    title: 'Keep the spec next to the code',
    body: "A comment can be wrong forever. A contract is checked, so it can't drift, and it's a precise brief for whoever writes the body: a teammate or an AI assistant.",
  },
];

const FEATURES = [
  {
    title: 'Check what a method did',
    body: (
      <>
        <code>Ensures</code> takes any C# expression over the result, the arguments and the object.{' '}
        <code>Old()</code> gives you a value as it was before the call.
      </>
    ),
    link: {to: '/contracts', label: 'Writing contracts'},
    code: [{language: 'csharp', text: ENSURES}],
  },
  {
    title: 'Get property tests from your contracts',
    body: (
      <>
        <code>Pledge.Testing</code> generates inputs for each method, skips the ones the preconditions reject, and
        checks the rest against the postconditions. Failures shrink to the smallest input.
      </>
    ),
    link: {to: '/testing', label: 'Contracts as property tests'},
    code: [
      {language: 'csharp', text: PROPERTY_TEST},
      {language: 'text', text: PROPERTY_FAILURE},
    ],
  },
  {
    title: 'See whose bug it is',
    body: "A broken precondition is the caller's bug, so the message gives the caller's file and line. A broken postcondition is the method's.",
    link: {to: '/getting-started#break-it', label: 'See a violation'},
    code: [{language: 'text', text: BLAME}],
  },
  {
    title: 'On in Debug, gone in Release',
    body: (
      <>
        Release builds are byte-identical to builds without Pledge. Switch to <code>Observe</code> to log violations
        instead of throwing, and add contracts to code that&apos;s already in production.
      </>
    ),
    link: {to: '/modes', label: 'Enforcement modes'},
    code: [{language: 'xml', text: MODES}],
  },
];

const FACTS = [
  {title: 'One package', body: 'Runtime, generator, analyzers and code fixes.'},
  {title: 'Standard compiler', body: 'A source generator and interceptors. No IL rewriting.'},
  {title: 'Native AOT ready', body: 'Plain generated C#, no reflection.'},
  {title: 'MIT licensed', body: 'Free for any use. No paid tier.'},
];

// A placeholder mark until there is a real logo: a check between brackets, the same as static/img/logo.svg.
function Mark({className}) {
  return (
    <svg className={className} viewBox="0 0 32 32" fill="none" aria-hidden="true">
      <rect x="1" y="1" width="30" height="30" rx="8" fill="var(--ifm-color-primary)" />
      {/* Infima's --ifm-background-color is transparent in light mode, so the glyph takes the surface color. */}
      <g stroke="var(--ifm-background-surface-color)" strokeLinecap="round" strokeLinejoin="round">
        <path d="M11 9.5H9.5v13H11M21 9.5h1.5v13H21" strokeWidth="2" />
        <path d="m12.75 16.25 2.25 2.25 4.5-5" strokeWidth="2.4" />
      </g>
    </svg>
  );
}

function Section({overline, title, lead, className, children}) {
  return (
    <section className={`container ${styles.section} ${className ?? ''}`}>
      <p className={styles.overline}>{overline}</p>
      <Heading as="h2" className={styles.sectionTitle}>
        {title}
      </Heading>
      {lead ? <p className={styles.lead}>{lead}</p> : null}
      {children}
    </section>
  );
}

function Feature({title, body, link, code}) {
  return (
    <div className={styles.feature}>
      <div>
        <Heading as="h3">{title}</Heading>
        <p>{body}</p>
        <Link to={link.to}>{link.label} →</Link>
      </div>
      <div className={styles.featureCode}>
        {code.map(({language, text}) => (
          <div key={text} className={language === 'text' ? styles.wrap : undefined}>
            <CodeBlock language={language}>{text}</CodeBlock>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function Home() {
  return (
    <Layout
      title="Design by Contract for .NET"
      description="Write what a method needs and what it guarantees as plain C#. Pledge checks it on every call, with the compiler you already use.">
      <main>
        <header className={`container ${styles.hero}`}>
          <div className={styles.brand}>
            <Mark className={styles.mark} />
            <Heading as="h1" className={styles.wordmark}>
              Pledge
            </Heading>
            <span className={styles.alpha}>Alpha</span>
          </div>
          <p className={styles.heroLead}>
            Write what a method needs and what it guarantees as plain C#. Pledge checks it on every call, with the
            compiler you already use.
          </p>
          <ContractLift />
          <div className={styles.start}>
            <Link className="button button--primary button--lg" to="/getting-started">
              Get started
            </Link>
            <div className={styles.install}>
              <CodeBlock language="bash">dotnet add package Pledge --prerelease</CodeBlock>
            </div>
            <Link className={styles.startLink} to="/use-cases">
              When should I use it? →
            </Link>
          </div>
        </header>

        <Section
          overline="Why contracts"
          title="Why use contracts?"
          lead="Types say what goes in and out. Contracts say what a method needs and what it promises.">
          <div className={styles.columns}>
            {REASONS.map(({title, body}, index) => (
              <div key={title} className={styles.column}>
                <span className={styles.number}>{String(index + 1).padStart(2, '0')}</span>
                <Heading as="h3">{title}</Heading>
                <p>{body}</p>
              </div>
            ))}
          </div>
          <p className={styles.links}>
            <Link to="/use-cases">When to use Pledge →</Link>
            <Link to="/validation-vs-contracts">Validation or contracts? →</Link>
          </p>
        </Section>

        <Section
          overline="What's different"
          title="What Pledge adds"
          lead="Most .NET contract tools check arguments on the way in. Pledge also checks what the method did.">
          <div className={styles.features}>
            {FEATURES.map((feature) => (
              <Feature key={feature.title} {...feature} />
            ))}
          </div>
          <div className={`${styles.columns} ${styles.facts}`}>
            {FACTS.map(({title, body}) => (
              <div key={title} className={styles.column}>
                <Heading as="h3">{title}</Heading>
                <p>{body}</p>
              </div>
            ))}
          </div>
        </Section>

        <Section overline="Try it" title="Write your first contract" lead={<>Add the package, write a <code>Pledge.Requires</code> line and build.</>} className={styles.closing}>
          <div className={styles.actions}>
            <Link className="button button--primary button--lg" to="/getting-started">
              Get started
            </Link>
            <Link to="/comparison">Compare with other tools →</Link>
          </div>
        </Section>
      </main>
    </Layout>
  );
}
