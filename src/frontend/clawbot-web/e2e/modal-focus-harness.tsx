import { useLayoutEffect, useState, type ComponentType } from "react";
import { createRoot } from "react-dom/client";
import { Modal, type ModalProps } from "../src/shared/ui/Modal";

interface DismissibleModalProps extends ModalProps {
  readonly dismissible?: boolean;
}

const ModalWithDismissible = Modal as ComponentType<DismissibleModalProps>;

function ModalFocusHarness() {
  const [outerOpen, setOuterOpen] = useState(false);
  const [innerOpen, setInnerOpen] = useState(false);
  const [progressOpen, setProgressOpen] = useState(false);
  const [backgroundClickCount, setBackgroundClickCount] = useState(0);

  useLayoutEffect(() => {
    document.documentElement.dataset.modalFocusHarnessReady = "true";
    return () => {
      delete document.documentElement.dataset.modalFocusHarnessReady;
    };
  }, []);

  return (
    <>
      <section data-isolation-branch="root-background">
        <button
          type="button"
          data-testid="background-button"
          onClick={() => setBackgroundClickCount((count) => count + 1)}
        >
          Background action
        </button>
        <output data-testid="background-click-count">{backgroundClickCount}</output>
        <button type="button" data-testid="outer-opener" onClick={() => setOuterOpen(true)}>
          Open outer dialog
        </button>
        <button type="button" data-testid="progress-opener" onClick={() => setProgressOpen(true)}>
          Open progress dialog
        </button>
      </section>

      <Modal open={outerOpen} onClose={() => setOuterOpen(false)} title="Outer dialog">
        <button type="button" data-modal-initial-focus data-testid="outer-initial">
          Outer first control
        </button>
        <button type="button" data-testid="inner-opener" onClick={() => setInnerOpen(true)}>
          Open inner dialog
        </button>
        <button type="button">Outer last control</button>
      </Modal>

      <Modal open={innerOpen} onClose={() => setInnerOpen(false)} title="Inner dialog">
        <button type="button" data-modal-initial-focus data-testid="inner-initial">
          Inner first control
        </button>
        <button type="button">Inner last control</button>
      </Modal>

      <ModalWithDismissible
        open={progressOpen}
        onClose={() => setProgressOpen(false)}
        title="Progress dialog"
        dismissible={false}
        footer={(
          <button type="button" data-testid="finish-progress" onClick={() => setProgressOpen(false)}>
            Finish progress
          </button>
        )}
      >
        <p role="status" aria-live="polite">
          Processing remains visible
        </p>
      </ModalWithDismissible>
    </>
  );
}

export function mountModalFocusHarness(container: HTMLElement): void {
  createRoot(container).render(<ModalFocusHarness />);
}
